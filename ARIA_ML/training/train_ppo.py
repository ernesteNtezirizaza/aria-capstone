"""
training/train_ppo.py
=====================
PPO Training - 5 hyperparameter experiments.

All ML decisions live here:
  - Policy type (MultiInputPolicy)
  - Network architecture (pi=[256,256], vf=[256,256])
  - Hyperparameter experiments
  - Training loop
  - Evaluation and generalisation test
  - All 5 visualisation plots

The notebook is purely orchestration - it calls train_experiment(cfg)
and handles disk management, output copying, and session resumption.
"""

import os, sys, json, csv
import numpy as np
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import seaborn as sns

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from stable_baselines3 import PPO
from stable_baselines3.common.env_util import make_vec_env
from stable_baselines3.common.monitor import Monitor
from stable_baselines3.common.callbacks import (
    EvalCallback, CheckpointCallback, CallbackList, BaseCallback
)
from configs.config import (
    N_ENVS, EVAL_FREQ, N_EVAL_EPISODES,
    CHECKPOINTS_DIR, PLOTS_DIR, METRICS_DIR,
    ZONE_DEFINITIONS, PRIMARY_METRIC, TARGET_IMPROVEMENT, DISCOUNT_GAMMA,
    TOTAL_TIMESTEPS,    # single source of truth - set in config.py
)
from env.rwanda_env import RwandaReforestEnv
from env.cnn_extractor import ARIACNNExtractor

import gymnasium as _gym

class _Compat5Env(_gym.Env):
    """
    Wraps any env so step() always returns gymnasium 5-tuple.
    Inherits from gymnasium.Env so DummyVecEnv._patch_env() accepts it.
    """
    def __init__(self, env):
        super().__init__()
        self._env              = env
        self.observation_space = env.observation_space
        self.action_space      = env.action_space
        self.metadata          = getattr(env, "metadata", {})
        self.render_mode       = getattr(env, "render_mode", None)
        self.spec              = getattr(env, "spec", None)
        self.np_random         = getattr(env, "np_random", None)

    def reset(self, seed=None, options=None):
        result = self._env.reset(seed=seed, options=options)
        if isinstance(result, tuple) and len(result) == 2:
            return result          # (obs, info) - already correct
        return result, {}          # bare obs - add empty info

    def step(self, action):
        result = self._env.step(action)
        if len(result) == 5:
            return result          # already 5-tuple
        obs, reward, done, info = result
        return obs, reward, done, False, info   # expand to 5-tuple

    def close(self):
        return self._env.close()

    def render(self):
        return self._env.render()


# ── Policy configuration - single source of truth ─────────────────
PPO_POLICY    = "MultiInputPolicy"
PPO_NET_ARCH  = dict(pi=[256, 256], vf=[256, 256])
PPO_VF_COEF   = 0.5
PPO_GRAD_NORM = 0.5
PPO_GAE_LAMBDA= 0.95
PPO_SEED      = 42


os.makedirs(CHECKPOINTS_DIR, exist_ok=True)
os.makedirs(PLOTS_DIR,       exist_ok=True)
os.makedirs(METRICS_DIR,     exist_ok=True)

# ── Curriculum zone ordering - derived from zone registry ──────────
def _build_curriculum_order():
    """Sort training zone indices by suitability (easy first)."""
    registry_path = os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        "data", "zones", "zone_registry.json"
    )
    if not os.path.exists(registry_path):
        return list(range(36))
    with open(registry_path) as f:
        registry = json.load(f)
    train_zones = [z for z in registry if z["split"] == "train"]
    train_zones.sort(
        key=lambda z: z["mean_soil"] + (1.0 - z["mean_dist"]),
        reverse=True
    )
    return [z["array_index"] for z in train_zones]

CURRICULUM_ZONE_ORDER = _build_curriculum_order()


# ── Curriculum environment ─────────────────────────────────────────
class CurriculumEnv(RwandaReforestEnv):
    """
    Wraps RwandaReforestEnv to implement zone curriculum.
    Easy zones first, progressively unlocking harder zones.
    curriculum_progress (0→1) updated externally by CurriculumCallback.
    """
    def __init__(self, split="train", curriculum_progress=0.0, **kwargs):
        super().__init__(split=split, **kwargs)
        self.curriculum_progress = curriculum_progress

    def reset(self, seed=None, options=None):
        n_available = max(3, int(self.n_zones * self.curriculum_progress))
        available   = CURRICULUM_ZONE_ORDER[:n_available]
        chosen      = int(np.random.choice(available))
        self.zone_id = chosen
        obs, info    = super().reset(seed=seed, options=options)
        self.zone_id = None
        return obs, info


# ── Curriculum progress callback ───────────────────────────────────
class CurriculumCallback(BaseCallback):
    """Updates curriculum_progress on all envs every step."""
    def __init__(self, total_timesteps: int):
        super().__init__()
        self.total_timesteps = total_timesteps

    def _on_step(self) -> bool:
        progress = min(1.0, self.num_timesteps / self.total_timesteps)
        for env in self.training_env.envs:
            if hasattr(env, "curriculum_progress"):
                env.curriculum_progress = progress
            elif hasattr(env, "env") and hasattr(env.env, "curriculum_progress"):
                env.env.curriculum_progress = progress
        return True


# ── Entropy tracking callback ──────────────────────────────────────
class EntropyCallback(BaseCallback):
    """Records policy entropy and mean reward at every rollout end."""
    def __init__(self):
        super().__init__()
        self.entropy_log = []
        self.reward_log  = []
        self.steps_log   = []

    def _on_rollout_end(self):
        if hasattr(self.model, "logger") and self.model.logger:
            logs = self.model.logger.name_to_value
            if "train/entropy_loss" in logs:
                self.entropy_log.append(-float(logs["train/entropy_loss"]))
            if "rollout/ep_rew_mean" in logs:
                self.reward_log.append(float(logs["rollout/ep_rew_mean"]))
                self.steps_log.append(self.num_timesteps)
        return True

    def _on_step(self):
        return True


# ── 3 Experiments ─────────────────────────────────────────────
PPO_EXPERIMENTS = [
    {
        "exp": "01",
        "description": "Baseline + domain randomisation",
        "learning_rate": 0.0003, "gamma": 0.99,
        "n_steps": 4096, "batch_size": 256,
        "n_epochs": 10, "clip_range": 0.2, "ent_coef": 0.10,
        "use_curriculum": False,
        "effect": (
            "Baseline PPO with full zone randomisation every episode. "
            "Agent must learn terrain-agnostic strategy across all "
            "training zones. Direct fix for the generalisation failure."
        ),
    },
    {
        "exp": "02",
        "description": "Low LR + domain randomisation",
        "learning_rate": 0.0001, "gamma": 0.99,
        "n_steps": 4096, "batch_size": 256,
        "n_epochs": 10, "clip_range": 0.2, "ent_coef": 0.10,
        "use_curriculum": False,
        "effect": (
            "Conservative updates with full randomisation. "
            "Slower but finer gradient steps may find a more robust "
            "policy that generalises better to unseen terrain."
        ),
    },
    {
        "exp": "03",
        "description": "Low LR + stable fine-tuning",
        "learning_rate": 0.00005, "gamma": 0.99,
        "n_steps": 4096, "batch_size": 128,
        "n_epochs": 20, "clip_range": 0.1, "ent_coef": 0.10,
        "use_curriculum": False,
        "effect": (
            "Long rollout captures germination rewards across diverse "
            "zones. Tighter clip and low LR prevent instability. "
            "Best candidate for ecological metrics on unseen terrain."
        ),
    },
]

N_EXPERIMENTS = len(PPO_EXPERIMENTS)


# ── Single experiment training ─────────────────────────────────────
def train_experiment(cfg: dict, output_dir: str = None) -> dict:
    """
    Train one PPO experiment and return result dict.

    All PPO configuration (policy, architecture, callbacks) is
    defined here - the notebook just calls this function.

    Parameters
    ----------
    cfg        : experiment config dict from PPO_EXPERIMENTS
    output_dir : optional override for checkpoint directory
                 (used by notebook to save to ARIA_results)
    """
    exp_id   = cfg["exp"]
    ckpt_dir = output_dir or os.path.join(CHECKPOINTS_DIR, f"ppo_exp_{exp_id}")
    os.makedirs(ckpt_dir, exist_ok=True)

    use_curriculum = cfg.get("use_curriculum", False)

    print(f"\n── PPO Experiment {exp_id}/{len(PPO_EXPERIMENTS)} ──")
    print(f"   {cfg['description']}")
    print(f"   LR={cfg['learning_rate']}  gamma={cfg['gamma']}  "
          f"n_steps={cfg['n_steps']}  batch={cfg['batch_size']}  "
          f"clip={cfg['clip_range']}  ent={cfg['ent_coef']}  "
          f"curriculum={use_curriculum}")
    print(f"   Timesteps: {TOTAL_TIMESTEPS:,}")

    # ── Environment factory ────────────────────────────────────────
    if use_curriculum:
        def make_train_env():
            return CurriculumEnv(split="train", curriculum_progress=0.0)
    else:
        def make_train_env():
            return RwandaReforestEnv(split="train", zone_id=None)

    train_env = make_vec_env(make_train_env, n_envs=N_ENVS, seed=PPO_SEED)
    eval_env  = Monitor(RwandaReforestEnv(split="train", zone_id=None, seed=999))
    entropy_cb = EntropyCallback()

    callbacks = [
        EvalCallback(
            eval_env=eval_env,
            best_model_save_path=ckpt_dir,
            log_path=ckpt_dir,
            eval_freq=max(EVAL_FREQ // N_ENVS, 1),
            n_eval_episodes=N_EVAL_EPISODES,
            deterministic=True,
            verbose=0,
        ),
        CheckpointCallback(
            save_freq=max(TOTAL_TIMESTEPS // N_ENVS, 1),  # save once at end only
            save_path=ckpt_dir,
            name_prefix=f"ppo_exp{exp_id}",
            verbose=0,
        ),
        entropy_cb,
    ]
    if use_curriculum:
        callbacks.append(CurriculumCallback(total_timesteps=TOTAL_TIMESTEPS))

    # ── PPO model - policy and architecture defined here ──────────
    model = PPO(
        policy=PPO_POLICY,          # "MultiInputPolicy" for Dict obs spaces
        env=train_env,
        learning_rate=cfg["learning_rate"],
        gamma=cfg["gamma"],
        n_steps=cfg["n_steps"],
        batch_size=cfg["batch_size"],
        n_epochs=cfg["n_epochs"],
        clip_range=cfg["clip_range"],
        ent_coef=cfg["ent_coef"],
        vf_coef=PPO_VF_COEF,
        max_grad_norm=PPO_GRAD_NORM,
        gae_lambda=PPO_GAE_LAMBDA,
        policy_kwargs=dict(
            net_arch=PPO_NET_ARCH,
            features_extractor_class=ARIACNNExtractor,
            features_extractor_kwargs=dict(features_dim=256),
        ),
        verbose=1,
        seed=PPO_SEED,
        device="auto",
    )

    model.learn(
        total_timesteps=TOTAL_TIMESTEPS,
        callback=CallbackList(callbacks),
        progress_bar=False,
        reset_num_timesteps=True,
    )

    model.save(os.path.join(ckpt_dir, "ppo_final"))
    train_env.close()
    eval_env.close()

    # Aggressively clean intermediate checkpoints to save disk space
    import glob as _glob
    for _f in _glob.glob(os.path.join(ckpt_dir, "ppo_exp*.zip")):
        os.remove(_f)
    # Also clean from CHECKPOINTS_DIR
    for _f in _glob.glob(os.path.join(CHECKPOINTS_DIR, f"ppo_exp_{exp_id}", "ppo_exp*.zip")):
        os.remove(_f)

    eval_results = {}
    npz_path = os.path.join(ckpt_dir, "evaluations.npz")
    if os.path.exists(npz_path):
        data = np.load(npz_path)
        eval_results = {
            "timesteps":    data["timesteps"].tolist(),
            "mean_rewards": data["results"].mean(axis=1).tolist(),
        }

    result = {
        "exp":          exp_id,
        "description":  cfg["description"],
        "effect":       cfg["effect"],
        "config":       {k: v for k, v in cfg.items()
                         if k not in ("exp", "description", "effect")},
        "eval_results": eval_results,
        "entropy_log":  entropy_cb.entropy_log,
        "reward_log":   entropy_cb.reward_log,
        "steps_log":    entropy_cb.steps_log,
        "checkpoint":   ckpt_dir,
    }

    with open(os.path.join(ckpt_dir, "result.json"), "w") as f:
        json.dump(result, f, indent=2)

    rew  = eval_results.get("mean_rewards", [])
    best = max(rew) if rew else float("nan")
    print(f"   Exp {exp_id} complete - best reward: {best:.2f}")
    return result


# ── Generalisation test ────────────────────────────────────────────
def generalisation_test(best_ckpt_dir: str) -> dict:
    """Test best model on all 6 held-out eval zones. Saves CSV."""
    print("\nRunning generalisation test on 6 held-out eval zones...")

    best_path = os.path.join(best_ckpt_dir, "best_model")
    if not os.path.exists(best_path + ".zip"):
        print("  No best_model.zip found - skipping")
        return {}

    dummy_env = make_vec_env(lambda: RwandaReforestEnv(split="eval"), n_envs=1)
    model     = PPO.load(best_path, env=dummy_env)
    dummy_env.close()

    probe  = RwandaReforestEnv(split="eval", seed=0)
    n_eval = probe.n_zones
    probe.close()
    print(f"  Found {n_eval} eval zones (indices 0 to {n_eval-1})")

    eval_zone_defs = [z for z in ZONE_DEFINITIONS if z[5] == "eval"]
    results = {}

    for idx in range(n_eval):
        display = eval_zone_defs[idx][3] if idx < len(eval_zone_defs) else str(idx)
        label   = f"zone_{idx}_{display.replace(' ', '_')}"
        print(f"  Testing zone {idx} ({display})...", end=" ", flush=True)

        try:
            env        = Monitor(RwandaReforestEnv(zone_id=idx, split="eval", seed=42))
            ep_metrics = []
            for _ in range(N_EVAL_EPISODES):
                obs, _ = env.reset()
                done   = False
                info   = {}
                while not done:
                    # deterministic=False: stochastic policy for generalisation
                    # prevents EMERGENCY collapse on unseen eval zones
                    action, _ = model.predict(obs, deterministic=False)
                    obs, _, terminated, truncated, info = env.step(int(action))
                    done = terminated or truncated
                if "episode_metrics" in info:
                    ep_metrics.append(info["episode_metrics"])
            env.close()

            if ep_metrics:
                results[label] = {
                    k: round(float(np.mean([m.get(k, 0.0) for m in ep_metrics])), 4)
                    for k in ep_metrics[0]
                }
                pct = results[label].get(PRIMARY_METRIC, 0.0)
                print(f"{PRIMARY_METRIC} = {pct:.3f}")
            else:
                # Diagnostic: find out what action the agent takes on this zone
                print("no metrics - running action diagnostic...")
                from collections import Counter as _Counter
                _env2 = RwandaReforestEnv(zone_id=idx, split="eval", seed=42)
                _obs, _ = _env2.reset()
                _acts = []
                for _ in range(30):
                    _a, _ = model.predict(_obs, deterministic=True)
                    _acts.append(int(_a))
                    _obs, _, _t, _tr, _ = _env2.step(int(_a))
                    if _t or _tr:
                        break
                _env2.close()
                _names = {40:"HOVER",41:"ABORT",42:"COVER_DEPLOY",
                          43:"COVER_RETRACT",44:"ALT_UP",45:"ALT_DOWN",46:"EMERGENCY"}
                print(f"    Steps taken: {len(_acts)}")
                for _a, _c in _Counter(_acts).most_common(3):
                    _n = _names.get(_a, f"SEED(move={_a//5},sp={_a%5})")
                    print(f"    action {_a:3d} {_n:20s}: {_c}x")

        except Exception as e:
            print(f"skipped - {e}")
            continue

    # Save as CSV
    csv_path = os.path.join(METRICS_DIR, "generalisation.csv")
    if results:
        fieldnames = ["zone"] + list(next(iter(results.values())).keys())
        with open(csv_path, "w", newline="") as f:
            writer = csv.DictWriter(f, fieldnames=fieldnames)
            writer.writeheader()
            for zone, metrics in results.items():
                writer.writerow({"zone": zone, **metrics})
    print(f"  Results saved to {csv_path}")
    return results


# ── Plot 1 - Cumulative reward curves ─────────────────────────────
def plot_cumulative_rewards(all_results: list):
    fig, axes = plt.subplots(1, N_EXPERIMENTS, figsize=(22 * N_EXPERIMENTS / 5, 5))
    fig.suptitle(
        "ARIA PPO - Cumulative Reward Curves\n"
        f"{N_EXPERIMENTS} Experiments on RwandaReforestEnv ({TOTAL_TIMESTEPS:,} timesteps each)",
        fontsize=13, fontweight="bold", y=1.02
    )
    colours = plt.cm.tab10(np.linspace(0, 1, N_EXPERIMENTS))

    for i, res in enumerate(all_results):
        ax  = axes[i] if N_EXPERIMENTS > 1 else axes
        col = colours[i]
        er  = res.get("eval_results", {})
        ts  = er.get("timesteps",    [])
        rew = er.get("mean_rewards", [])

        if ts and rew:
            ax.plot(ts, rew, color=col, linewidth=2, zorder=3)
            ax.fill_between(ts, rew, alpha=0.15, color=col)
            best_idx = int(np.argmax(rew))
            ax.scatter(ts[best_idx], rew[best_idx], color=col, s=60, zorder=5)
            ax.axhline(y=max(rew), color=col, linestyle="--", alpha=0.4, linewidth=1)
            ax.text(0.97, 0.05, f"peak={max(rew):.1f}",
                    transform=ax.transAxes, ha="right", fontsize=8, color=col)
        else:
            ax.text(0.5, 0.5, "No data", ha="center", va="center",
                    transform=ax.transAxes, fontsize=9)

        cfg = res.get("config", {})
        cur = "CURR" if cfg.get("use_curriculum") else "DR"
        ax.set_title(
            f"Exp {res['exp']}: {res['description'][:28]}\n"
            f"LR={cfg.get('learning_rate')}  ent={cfg.get('ent_coef')}  [{cur}]",
            fontsize=7.5, fontweight="bold", color=col
        )
        ax.set_xlabel("Training timesteps", fontsize=7)
        ax.set_ylabel("Mean episode reward", fontsize=7)
        ax.tick_params(labelsize=7)
        ax.grid(True, alpha=0.3)
        ax.spines["top"].set_visible(False)
        ax.spines["right"].set_visible(False)

    plt.tight_layout()
    path = os.path.join(PLOTS_DIR, "plot1_cumulative_rewards.png")
    plt.savefig(path, dpi=150, bbox_inches="tight")
    plt.close()
    print(f" Plot 1 saved: {path}")


# ── Plot 2 - Entropy curves ────────────────────────────────────────
def plot_entropy_curves(all_results: list):
    fig, axes = plt.subplots(1, 2, figsize=(14, 6))
    fig.suptitle(
        "ARIA PPO - Policy Gradient Entropy Curves\n"
        "Effect of Entropy Coefficient on Exploration Behaviour",
        fontsize=13, fontweight="bold"
    )
    groups = [
        ("ent_coef = 0.00  (No entropy bonus)",
         [r for r in all_results if r["config"].get("ent_coef", 0) == 0.0],
         "#4CAF50"),
        ("ent_coef = 0.01  (Light entropy bonus)",
         [r for r in all_results if r["config"].get("ent_coef", 0) > 0.0],
         "#2196F3"),
    ]
    for ax, (title, group, col) in zip(axes, groups):
        has_data = False
        for res in group:
            elog = res.get("entropy_log", [])
            if elog:
                smoothed = np.convolve(elog, np.ones(10)/10, mode="valid")
                ax.plot(smoothed, color=col, alpha=0.75, linewidth=1.8,
                        label=f"Exp {res['exp']}")
                has_data = True
        if not has_data:
            ax.text(0.5, 0.5, "No data", ha="center", va="center",
                    transform=ax.transAxes, fontsize=10)
        ax.set_title(title, fontsize=9, fontweight="bold", color=col)
        ax.set_xlabel("Rollouts (smoothed, window=10)", fontsize=8)
        ax.set_ylabel("Policy entropy H(π)", fontsize=8)
        ax.legend(fontsize=8)
        ax.grid(True, alpha=0.3)
        ax.spines["top"].set_visible(False)
        ax.spines["right"].set_visible(False)

    plt.tight_layout()
    path = os.path.join(PLOTS_DIR, "plot2_entropy_curves.png")
    plt.savefig(path, dpi=150, bbox_inches="tight")
    plt.close()
    print(f" Plot 2 saved: {path}")


# ── Plot 3 - Convergence comparison ───────────────────────────────
def plot_convergence(all_results: list):
    fig, ax = plt.subplots(figsize=(14, 7))
    ax.set_title(
        "ARIA PPO - Convergence Comparison\n"
        f"All {N_EXPERIMENTS} Experiments: Domain Randomisation vs Curriculum Learning",
        fontsize=13, fontweight="bold"
    )

    colours    = plt.cm.tab10(np.linspace(0, 1, N_EXPERIMENTS))
    # Best experiment = highest PEAK reward across all timesteps
    best_peak  = -np.inf
    best_exp   = ""
    best_ts    = []
    best_smooth= []

    for res, col in zip(all_results, colours):
        er  = res.get("eval_results", {})
        ts  = er.get("timesteps",    [])
        rew = er.get("mean_rewards", [])
        if not ts:
            continue

        window   = max(3, len(rew) // 10)
        smoothed = np.convolve(rew, np.ones(window)/window, mode="valid")
        ts_s     = ts[:len(smoothed)]
        cfg      = res["config"]
        cur      = "CURR" if cfg.get("use_curriculum") else "DR"
        label    = (f"Exp {res['exp']}: LR={cfg['learning_rate']} "
                    f"ent={cfg['ent_coef']} n={cfg['n_steps']} [{cur}]")

        ax.plot(ts_s, smoothed, color=col, linewidth=1.5, alpha=0.7, label=label)

        # Use peak reward to identify best - not final smoothed value
        peak = max(rew) if rew else -np.inf
        if peak > best_peak:
            best_peak   = peak
            best_exp    = f"Exp {res['exp']}"
            best_ts     = ts_s
            best_smooth = smoothed

    if best_ts:
        ax.plot(best_ts, best_smooth, color="black", linewidth=3.5,
                alpha=0.9, label=f"BEST: {best_exp}", zorder=10)
        ax.annotate(
            f"Best: {best_exp}\nPeak reward: {best_peak:.1f}",
            xy=(best_ts[int(np.argmax(best_smooth))],
                best_smooth[int(np.argmax(best_smooth))]),
            xytext=(-120, 30), textcoords="offset points",
            fontsize=10, fontweight="bold",
            arrowprops=dict(arrowstyle="->", color="black"),
            bbox=dict(boxstyle="round,pad=0.3", fc="lightyellow", ec="orange"),
        )

    ax.set_xlabel("Training timesteps", fontsize=11)
    ax.set_ylabel("Mean episode reward (smoothed)", fontsize=11)
    ax.legend(fontsize=8, loc="lower right", ncol=1, framealpha=0.9)
    ax.grid(True, alpha=0.3)
    ax.spines["top"].set_visible(False)
    ax.spines["right"].set_visible(False)

    plt.tight_layout()
    path = os.path.join(PLOTS_DIR, "plot3_convergence.png")
    plt.savefig(path, dpi=150, bbox_inches="tight")
    plt.close()
    print(f" Plot 3 saved: {path}")


# ── Plot 4 - Generalisation test ──────────────────────────────────
def plot_generalisation(gen_results: dict):
    if not gen_results:
        print("  No generalisation results - skipping Plot 4")
        return

    zones   = list(gen_results.keys())
    metrics = ["pct_suitable_seeded", "mean_soil_score",
               "species_entropy", "seasonal_rain_score", "reseeding_count"]
    metrics = [m for m in metrics if m in gen_results[zones[0]]]

    fig, axes = plt.subplots(1, len(metrics), figsize=(4*len(metrics), 7))
    if len(metrics) == 1:
        axes = [axes]

    fig.suptitle(
        "ARIA PPO - Generalisation Test\n"
        "Best Model on 6 Held-Out Rwanda Terrain Zones",
        fontsize=13, fontweight="bold"
    )
    palette     = sns.color_palette("viridis", len(zones))
    zone_labels = []
    for z in zones:
        parts = z.split("_", 2)
        zone_labels.append(parts[2].replace("_", " ")[:15] if len(parts) >= 3 else z)

    for ax, metric in zip(axes, metrics):
        values  = [gen_results[z].get(metric, 0.0) for z in zones]
        max_val = max(values) if max(values) > 0 else 1.0
        bars    = ax.bar(zone_labels, values, color=palette,
                         edgecolor="white", linewidth=0.8, zorder=3)
        for bar, val in zip(bars, values):
            ax.text(bar.get_x() + bar.get_width()/2,
                    bar.get_height() + max_val*0.02,
                    f"{val:.3f}", ha="center", va="bottom",
                    fontsize=8, fontweight="bold")
        if metric == PRIMARY_METRIC:
            ax.axhline(y=TARGET_IMPROVEMENT, color="red",
                       linestyle="--", linewidth=2,
                       label=f"Target ≥ {TARGET_IMPROVEMENT}")
            ax.legend(fontsize=9)
        ax.set_title(metric.replace("_", "\n").title(), fontsize=9, fontweight="bold")
        ax.set_xlabel("Evaluation Zone", fontsize=8)
        ax.set_ylabel("Score", fontsize=8)
        ax.tick_params(axis="x", rotation=35, labelsize=6)
        ax.set_ylim(0, max_val * 1.25)
        ax.grid(True, axis="y", alpha=0.3, zorder=0)
        ax.spines["top"].set_visible(False)
        ax.spines["right"].set_visible(False)

    plt.tight_layout()
    path = os.path.join(PLOTS_DIR, "plot4_generalisation.png")
    plt.savefig(path, dpi=150, bbox_inches="tight")
    plt.close()
    print(f" Plot 4 saved: {path}")


# ── Plot 5 - Hyperparameter summary table ─────────────────────────
def plot_hyperparameter_table(all_results: list):
    fig, ax = plt.subplots(figsize=(22, 5))
    ax.axis("off")
    fig.suptitle(
        "ARIA PPO - Hyperparameter Experiments Summary Table\n"
        "5 Generalisation Experiments - Domain Randomisation + Curriculum Learning",
        fontsize=12, fontweight="bold"
    )
    col_labels = ["Exp", "Description", "LR", "Gamma",
                  "n_steps", "Batch", "Clip", "Entropy",
                  "Curriculum", "Peak\nReward", "Key Effect"]
    rows = []
    for res in all_results:
        cfg    = res.get("config", {})
        rew    = res.get("eval_results", {}).get("mean_rewards", [])
        peak   = f"{max(rew):.1f}" if rew else "N/A"
        effect = res.get("effect", "")[:52] + ("..." if len(res.get("effect","")) > 52 else "")
        rows.append([
            res["exp"],
            res["description"][:32] + ("..." if len(res["description"]) > 32 else ""),
            cfg.get("learning_rate"), cfg.get("gamma"),
            cfg.get("n_steps"),       cfg.get("batch_size"),
            cfg.get("clip_range"),    cfg.get("ent_coef"),
            "Yes" if cfg.get("use_curriculum") else "No",
            peak, effect,
        ])

    table = ax.table(cellText=rows, colLabels=col_labels,
                     cellLoc="center", loc="center", bbox=[0, 0, 1, 1])
    table.auto_set_font_size(False)
    table.set_fontsize(7.5)

    for j in range(len(col_labels)):
        table[0, j].set_facecolor("#1B5E20")
        table[0, j].set_text_props(color="white", fontweight="bold")
        table[0, j].set_height(0.15)

    for i in range(1, len(rows)+1):
        fc = "#E8F5E9" if i % 2 == 0 else "#FFFFFF"
        for j in range(len(col_labels)):
            table[i, j].set_facecolor(fc)
            table[i, j].set_height(0.12)

    peaks = [r[9] for r in rows if r[9] != "N/A"]
    if peaks:
        best_p = max(peaks, key=lambda x: float(x))
        for i, r in enumerate(rows):
            if r[9] == best_p:
                table[i+1, 9].set_facecolor("#FFD700")
                table[i+1, 9].set_text_props(fontweight="bold")

    table.auto_set_column_width(list(range(len(col_labels))))
    plt.tight_layout()
    path = os.path.join(PLOTS_DIR, "plot5_hyperparameter_table.png")
    plt.savefig(path, dpi=150, bbox_inches="tight")
    plt.close()
    print(f" Plot 5 saved: {path}")


# ── Save experiment summary as CSV ────────────────────────────────
def save_experiment_csv(all_results: list):
    csv_path = os.path.join(METRICS_DIR, "all_experiments.csv")
    with open(csv_path, "w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["exp","description","learning_rate","gamma",
                         "n_steps","batch_size","clip_range","ent_coef",
                         "use_curriculum","peak_reward"])
        for r in all_results:
            cfg = r.get("config", {})
            rew = r.get("eval_results", {}).get("mean_rewards", [])
            writer.writerow([
                r["exp"], r["description"],
                cfg.get("learning_rate"), cfg.get("gamma"),
                cfg.get("n_steps"), cfg.get("batch_size"),
                cfg.get("clip_range"), cfg.get("ent_coef"),
                cfg.get("use_curriculum"),
                round(max(rew), 2) if rew else "N/A"
            ])
    print(f" Experiments summary saved to {csv_path}")


# ── Main pipeline (for running train_ppo.py directly) ─────────────
def run():
    print("=" * 60)
    print("ARIA - PPO Training - Generalisation Experiments")
    print(f"Policy       : {PPO_POLICY}")
    print(f"Architecture : pi={PPO_NET_ARCH['pi']}  vf={PPO_NET_ARCH['vf']}")
    print(f"Timesteps    : {TOTAL_TIMESTEPS:,} per experiment")
    print("=" * 60)

    all_results = []
    best_reward = -np.inf
    best_dir    = None

    for cfg in PPO_EXPERIMENTS:
        result = train_experiment(cfg)
        all_results.append(result)
        rew = result.get("eval_results", {}).get("mean_rewards", [])
        if rew and max(rew) > best_reward:
            best_reward = max(rew)
            best_dir    = result["checkpoint"]

    save_experiment_csv(all_results)

    print("\n" + "=" * 60)
    print("Generating plots...")
    plot_cumulative_rewards(all_results)
    plot_entropy_curves(all_results)
    plot_convergence(all_results)
    plot_hyperparameter_table(all_results)

    if best_dir:
        gen_results = generalisation_test(best_dir)
        plot_generalisation(gen_results)

    print("\n" + "=" * 60)
    print(f"  Best reward: {best_reward:.2f}  |  Best dir: {best_dir}")
    print("=" * 60)


if __name__ == "__main__":
    run()
