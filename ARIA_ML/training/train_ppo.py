"""
training/train_ppo.py
=====================
PPO Training — 5 hyperparameter experiments.

Each experiment varies one or more hyperparameters to show
the effect on learning behaviour. Results include:
  - Cumulative reward curves (all 5 in subplots)
  - Entropy curves (policy gradient entropy per experiment)
  - Convergence plot (best experiment highlighted)
  - Generalisation test (best model on 6 eval zones)
  - Hyperparameter summary table

Run: python training/train_ppo.py
"""

import os, sys, json
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
    EVAL_ZONE_IDS, PRIMARY_METRIC, TARGET_IMPROVEMENT, DISCOUNT_GAMMA
)
from env.rwanda_env import RwandaReforestEnv

TOTAL_TIMESTEPS = 200_000  # 200k timesteps per experiment

os.makedirs(CHECKPOINTS_DIR, exist_ok=True)
os.makedirs(PLOTS_DIR,       exist_ok=True)
os.makedirs(METRICS_DIR,     exist_ok=True)

# ── 5 PPO Hyperparameter Experiments ──────────────────────────────
PPO_EXPERIMENTS = [
    {
        "exp": "01",
        "description": "Default SB3 PPO — baseline",
        "learning_rate": 0.0003, "gamma": 0.99,
        "n_steps": 2048, "batch_size": 64,
        "n_epochs": 10,  "clip_range": 0.2, "ent_coef": 0.00,
        "effect": (
            "Baseline configuration from SB3 defaults. "
            "Stable learning with moderate convergence speed. "
            "All other experiments are compared against this."
        ),
    },
    {
        "exp": "02",
        "description": "Low learning rate — conservative updates",
        "learning_rate": 0.0001, "gamma": 0.99,
        "n_steps": 2048, "batch_size": 64,
        "n_epochs": 10,  "clip_range": 0.2, "ent_coef": 0.00,
        "effect": (
            "Lower LR reduces step size per update. "
            "More stable but converges slower. "
            "Useful when reward signal is noisy (multi-component reward)."
        ),
    },
    {
        "exp": "03",
        "description": "High learning rate — aggressive updates",
        "learning_rate": 0.001,  "gamma": 0.99,
        "n_steps": 2048, "batch_size": 64,
        "n_epochs": 10,  "clip_range": 0.2, "ent_coef": 0.00,
        "effect": (
            "Higher LR takes larger gradient steps. "
            "Faster early learning but risks instability. "
            "PPO clipping provides some protection but may still diverge."
        ),
    },
    {
        "exp": "04",
        "description": "Light entropy bonus — balanced exploration",
        "learning_rate": 0.0003, "gamma": 0.99,
        "n_steps": 2048, "batch_size": 64,
        "n_epochs": 10,  "clip_range": 0.2, "ent_coef": 0.01,
        "effect": (
            "Small entropy coefficient encourages policy to maintain "
            "diversity in action selection. "
            "Helps drone explore species combinations and zone coverage."
        ),
    },
    {
        "exp": "05",
        "description": "Long rollout — best for delayed rewards",
        "learning_rate": 0.0001, "gamma": 0.99,
        "n_steps": 4096, "batch_size": 128,
        "n_epochs": 20,  "clip_range": 0.1, "ent_coef": 0.01,
        "effect": (
            "Longer rollout (4096 steps) captures germination events "
            "that fire 60-150 steps after planting. "
            "More data per update and tighter clip for stability. "
            "Expected best ecological metrics."
        ),
    },
]


# ── Entropy tracking callback ──────────────────────────────────────
class EntropyCallback(BaseCallback):
    """
    Records policy entropy and mean reward at every rollout end.
    Used to generate entropy curves and reward curves for plotting.
    """
    def __init__(self):
        super().__init__()
        self.entropy_log = []
        self.reward_log  = []
        self.steps_log   = []

    def _on_rollout_end(self):
        if hasattr(self.model, "logger") and self.model.logger:
            logs = self.model.logger.name_to_value
            if "train/entropy_loss" in logs:
                self.entropy_log.append(
                    -float(logs["train/entropy_loss"])
                )
            if "rollout/ep_rew_mean" in logs:
                self.reward_log.append(
                    float(logs["rollout/ep_rew_mean"])
                )
                self.steps_log.append(self.num_timesteps)
        return True

    def _on_step(self):
        return True


# ── Single experiment training ─────────────────────────────────────
def train_experiment(cfg: dict) -> dict:
    exp_id   = cfg["exp"]
    ckpt_dir = os.path.join(CHECKPOINTS_DIR, f"ppo_exp_{exp_id}")
    os.makedirs(ckpt_dir, exist_ok=True)

    print(f"\n── PPO Experiment {exp_id}/05 ──────────────────────────")
    print(f"   {cfg['description']}")
    print(f"   LR={cfg['learning_rate']}  "
          f"gamma={cfg['gamma']}  "
          f"n_steps={cfg['n_steps']}  "
          f"batch={cfg['batch_size']}  "
          f"clip={cfg['clip_range']}  "
          f"ent={cfg['ent_coef']}")
    print(f"   Timesteps: {TOTAL_TIMESTEPS:,}")

    def make_train_env():
        return RwandaReforestEnv(split="train")

    train_env  = make_vec_env(make_train_env, n_envs=N_ENVS)
    eval_env   = Monitor(RwandaReforestEnv(split="train", seed=999))
    entropy_cb = EntropyCallback()

    eval_cb = EvalCallback(
        eval_env=eval_env,
        best_model_save_path=ckpt_dir,
        log_path=ckpt_dir,
        eval_freq=max(EVAL_FREQ // N_ENVS, 1),
        n_eval_episodes=N_EVAL_EPISODES,
        deterministic=True,
        verbose=0,
    )

    checkpoint_cb = CheckpointCallback(
        save_freq=max(EVAL_FREQ * 2 // N_ENVS, 1),
        save_path=ckpt_dir,
        name_prefix=f"ppo_exp{exp_id}",
        verbose=0,
    )

    model = PPO(
        policy="MultiInputPolicy",
        env=train_env,
        learning_rate=cfg["learning_rate"],
        gamma=cfg["gamma"],
        n_steps=cfg["n_steps"],
        batch_size=cfg["batch_size"],
        n_epochs=cfg["n_epochs"],
        clip_range=cfg["clip_range"],
        ent_coef=cfg["ent_coef"],
        vf_coef=0.5,
        max_grad_norm=0.5,
        gae_lambda=0.95,
        policy_kwargs=dict(
            net_arch=dict(pi=[256, 256], vf=[256, 256])
        ),
        verbose=1,
        seed=42,
        device="auto",
    )

    model.learn(
        total_timesteps=TOTAL_TIMESTEPS,
        callback=CallbackList([eval_cb, checkpoint_cb, entropy_cb]),
        progress_bar=False,
        reset_num_timesteps=True,
    )

    final_path = os.path.join(ckpt_dir, "ppo_final")
    model.save(final_path)
    train_env.close()
    eval_env.close()

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
        "config": {
            k: v for k, v in cfg.items()
            if k not in ("exp", "description", "effect")
        },
        "eval_results": eval_results,
        "entropy_log":  entropy_cb.entropy_log,
        "reward_log":   entropy_cb.reward_log,
        "steps_log":    entropy_cb.steps_log,
        "checkpoint":   ckpt_dir,
    }

    with open(os.path.join(ckpt_dir, "result.json"), "w") as f:
        json.dump(result, f, indent=2)

    er   = eval_results.get("mean_rewards", [])
    best = max(er) if er else float("nan")
    print(f"   Experiment {exp_id} complete. Best eval reward: {best:.2f}")
    return result


# ── Generalisation test ────────────────────────────────────────────
def generalisation_test(best_ckpt_dir: str) -> dict:
    print("\nRunning generalisation test on 6 held-out eval zones...")

    best_path = os.path.join(best_ckpt_dir, "best_model")
    if not os.path.exists(best_path + ".zip"):
        print("  No best_model.zip found — skipping generalisation test")
        return {}

    results = {}
    for zone_id in EVAL_ZONE_IDS:
        print(f"  Testing zone {zone_id}...", end=" ", flush=True)

        env       = Monitor(RwandaReforestEnv(zone_id=zone_id, split="eval", seed=42))
        dummy_env = make_vec_env(lambda: RwandaReforestEnv(split="eval"), n_envs=1)
        model     = PPO.load(best_path, env=dummy_env)

        ep_metrics = []
        for _ in range(N_EVAL_EPISODES):
            obs, _ = env.reset()
            done   = False
            while not done:
                action, _ = model.predict(obs, deterministic=True)
                obs, _, terminated, truncated, info = env.step(int(action))
                done = terminated or truncated
            if "episode_metrics" in info:
                ep_metrics.append(info["episode_metrics"])

        env.close()
        dummy_env.close()

        if ep_metrics:
            results[f"zone_{zone_id}"] = {
                k: round(float(np.mean([m.get(k, 0.0)
                                        for m in ep_metrics])), 4)
                for k in ep_metrics[0]
            }
            pct = results[f"zone_{zone_id}"].get(PRIMARY_METRIC, 0.0)
            print(f"{PRIMARY_METRIC} = {pct:.3f}")
        else:
            print("no metrics")

    with open(os.path.join(METRICS_DIR, "generalisation.json"), "w") as f:
        json.dump(results, f, indent=2)

    print(f"  Generalisation results saved to {METRICS_DIR}")
    return results


# ── Plot 1 — Cumulative reward curves ─────────────────────────────
def plot_cumulative_rewards(all_results: list):
    fig, axes = plt.subplots(1, 5, figsize=(22, 5))
    fig.suptitle(
        "ARIA PPO — Cumulative Reward Curves\n"
        "5 Hyperparameter Experiments on RwandaReforestEnv "
        f"({TOTAL_TIMESTEPS:,} timesteps each)",
        fontsize=13, fontweight="bold", y=1.02
    )
    axes    = axes.flatten()
    colours = plt.cm.tab10(np.linspace(0, 1, 5))

    for i, res in enumerate(all_results):
        ax  = axes[i]
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
            ax.text(0.97, 0.05, f"best={max(rew):.1f}",
                    transform=ax.transAxes, ha="right", fontsize=8, color=col)
        else:
            ax.text(0.5, 0.5, "No data\n(train first)",
                    ha="center", va="center", transform=ax.transAxes, fontsize=9)

        cfg = res.get("config", {})
        ax.set_title(
            f"Exp {res['exp']}: {res['description'][:30]}\n"
            f"LR={cfg.get('learning_rate')}  "
            f"γ={cfg.get('gamma')}  "
            f"ent={cfg.get('ent_coef')}",
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


# ── Plot 2 — PG Entropy curves ────────────────────────────────────
def plot_entropy_curves(all_results: list):
    fig, axes = plt.subplots(1, 2, figsize=(14, 6))
    fig.suptitle(
        "ARIA PPO — Policy Gradient Entropy Curves\n"
        "Effect of Entropy Coefficient on Exploration Behaviour",
        fontsize=13, fontweight="bold"
    )

    groups = [
        ("ent_coef = 0.00\n(No entropy bonus — pure exploitation)",
         [r for r in all_results if r["config"].get("ent_coef", 0) == 0.0],
         "#4CAF50"),
        ("ent_coef = 0.01\n(Light entropy — balanced exploration)",
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
                        label=f"Exp {res['exp']} "
                              f"(ent={res['config'].get('ent_coef',0)})")
                has_data = True

        if not has_data:
            ax.text(0.5, 0.5, "No data\n(train first)",
                    ha="center", va="center",
                    transform=ax.transAxes, fontsize=10)

        ax.set_title(title, fontsize=9, fontweight="bold", color=col)
        ax.set_xlabel("Training rollouts (smoothed, window=10)", fontsize=8)
        ax.set_ylabel("Policy entropy H(π)", fontsize=8)
        ax.legend(fontsize=8, loc="upper right")
        ax.grid(True, alpha=0.3)
        ax.spines["top"].set_visible(False)
        ax.spines["right"].set_visible(False)

    fig.text(
        0.5, -0.04,
        "Higher entropy = more diverse action selection = more exploration. "
        "Lower entropy = more deterministic policy = faster exploitation.",
        ha="center", fontsize=9, style="italic", color="gray"
    )

    plt.tight_layout()
    path = os.path.join(PLOTS_DIR, "plot2_entropy_curves.png")
    plt.savefig(path, dpi=150, bbox_inches="tight")
    plt.close()
    print(f" Plot 2 saved: {path}")


# ── Plot 3 — Convergence comparison ──────────────────────────────
def plot_convergence(all_results: list):
    fig, ax = plt.subplots(figsize=(14, 7))
    ax.set_title(
        "ARIA PPO — Convergence Comparison\n"
        "All 5 Experiments: Which Hyperparameters Converge Fastest?",
        fontsize=13, fontweight="bold"
    )

    colours     = plt.cm.tab10(np.linspace(0, 1, 5))
    best_final  = -np.inf
    best_exp    = ""
    best_ts     = []
    best_smooth = []

    for res, col in zip(all_results, colours):
        er  = res.get("eval_results", {})
        ts  = er.get("timesteps",    [])
        rew = er.get("mean_rewards", [])
        if not ts:
            continue

        window   = max(3, len(rew)//10)
        smoothed = np.convolve(rew, np.ones(window)/window, mode="valid")
        ts_s     = ts[:len(smoothed)]
        cfg      = res["config"]
        label    = (f"Exp {res['exp']}: "
                    f"LR={cfg['learning_rate']} "
                    f"γ={cfg['gamma']} "
                    f"ent={cfg['ent_coef']} "
                    f"n={cfg['n_steps']}")

        ax.plot(ts_s, smoothed, color=col, linewidth=1.5, alpha=0.7, label=label)

        if len(smoothed) > 0 and smoothed[-1] > best_final:
            best_final  = smoothed[-1]
            best_exp    = f"Exp {res['exp']}"
            best_ts     = ts_s
            best_smooth = smoothed

    if best_ts:
        ax.plot(best_ts, best_smooth, color="black", linewidth=3.5,
                alpha=0.9, label=f"BEST: {best_exp}", zorder=10)
        ax.annotate(
            f"Best: {best_exp}\nFinal reward: {best_final:.1f}",
            xy=(best_ts[-1], best_smooth[-1]),
            xytext=(-120, 30),
            textcoords="offset points",
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


# ── Plot 4 — Generalisation test ─────────────────────────────────
def plot_generalisation(gen_results: dict):
    if not gen_results:
        print("  No generalisation results — skipping Plot 4")
        return

    zones   = list(gen_results.keys())
    metrics = ["pct_suitable_seeded", "mean_soil_score",
               "species_entropy", "seasonal_rain_score", "reseeding_count"]
    metrics = [m for m in metrics if m in gen_results[zones[0]]]

    fig, axes = plt.subplots(1, len(metrics), figsize=(4*len(metrics), 7))
    if len(metrics) == 1:
        axes = [axes]

    fig.suptitle(
        "ARIA PPO — Generalisation Test\n"
        "Best Model on 6 Held-Out Rwanda Terrain Zones "
        "(Never Seen During Training)",
        fontsize=13, fontweight="bold"
    )

    palette     = sns.color_palette("viridis", len(zones))
    zone_labels = [f"Zone {z.split('_')[1]}" for z in zones]

    for ax, metric in zip(axes, metrics):
        values = [gen_results[z].get(metric, 0.0) for z in zones]
        bars   = ax.bar(zone_labels, values, color=palette,
                        edgecolor="white", linewidth=0.8, zorder=3)

        for bar, val in zip(bars, values):
            ax.text(bar.get_x() + bar.get_width()/2,
                    bar.get_height() + max(values)*0.02,
                    f"{val:.3f}", ha="center", va="bottom",
                    fontsize=8, fontweight="bold")

        if metric == PRIMARY_METRIC:
            ax.axhline(y=TARGET_IMPROVEMENT, color="red",
                       linestyle="--", linewidth=2,
                       label=f"Target ≥ {TARGET_IMPROVEMENT}")
            ax.legend(fontsize=9)

        ax.set_title(metric.replace("_", "\n").title(),
                     fontsize=9, fontweight="bold")
        ax.set_xlabel("Evaluation Zone", fontsize=8)
        ax.set_ylabel("Score", fontsize=8)
        ax.tick_params(axis="x", rotation=35, labelsize=7)
        ax.set_ylim(0, max(values)*1.25 if max(values) > 0 else 1.0)
        ax.grid(True, axis="y", alpha=0.3, zorder=0)
        ax.spines["top"].set_visible(False)
        ax.spines["right"].set_visible(False)

    plt.tight_layout()
    path = os.path.join(PLOTS_DIR, "plot4_generalisation.png")
    plt.savefig(path, dpi=150, bbox_inches="tight")
    plt.close()
    print(f" Plot 4 saved: {path}")


# ── Plot 5 — Hyperparameter summary table ─────────────────────────
def plot_hyperparameter_table(all_results: list):
    fig, ax = plt.subplots(figsize=(20, 5))
    ax.axis("off")
    fig.suptitle(
        "ARIA PPO — Hyperparameter Experiments Summary Table\n"
        "5 Experiments — Effects of Tuning "
        "(learning_rate, gamma, entropy, n_steps, clip_range)",
        fontsize=12, fontweight="bold"
    )

    col_labels = ["Exp", "Description", "Learning\nRate", "Gamma",
                  "n_steps\n(buffer)", "Batch\nSize", "Clip\nRange",
                  "Entropy\n(ent_coef)", "Best\nReward", "Key Effect"]

    rows = []
    for res in all_results:
        cfg    = res.get("config", {})
        er     = res.get("eval_results", {})
        rew    = er.get("mean_rewards", [])
        best   = f"{max(rew):.1f}" if rew else "N/A"
        effect = res.get("effect", "")
        if len(effect) > 60:
            effect = effect[:57] + "..."
        rows.append([
            res["exp"],
            res["description"][:35] + ("..." if len(res["description"]) > 35 else ""),
            cfg.get("learning_rate", "?"), cfg.get("gamma", "?"),
            cfg.get("n_steps", "?"),       cfg.get("batch_size", "?"),
            cfg.get("clip_range", "?"),    cfg.get("ent_coef", "?"),
            best, effect,
        ])

    table = ax.table(cellText=rows, colLabels=col_labels,
                     cellLoc="center", loc="center", bbox=[0, 0, 1, 1])
    table.auto_set_font_size(False)
    table.set_fontsize(7.5)

    for j in range(len(col_labels)):
        table[0, j].set_facecolor("#1B5E20")
        table[0, j].set_text_props(color="white", fontweight="bold")
        table[0, j].set_height(0.15)

    for i in range(1, len(rows) + 1):
        fc = "#E8F5E9" if i % 2 == 0 else "#FFFFFF"
        for j in range(len(col_labels)):
            table[i, j].set_facecolor(fc)
            table[i, j].set_height(0.12)

    rewards = [r[8] for r in rows if r[8] != "N/A"]
    if rewards:
        best_r = max(rewards, key=lambda x: float(x))
        for i, r in enumerate(rows):
            if r[8] == best_r:
                table[i+1, 8].set_facecolor("#FFD700")
                table[i+1, 8].set_text_props(fontweight="bold")

    table.auto_set_column_width([0, 1, 2, 3, 4, 5, 6, 7, 8, 9])

    plt.tight_layout()
    path = os.path.join(PLOTS_DIR, "plot5_hyperparameter_table.png")
    plt.savefig(path, dpi=150, bbox_inches="tight")
    plt.close()
    print(f" Plot 5 saved: {path}")


# ── Main pipeline ──────────────────────────────────────────────────
def run():
    print("=" * 60)
    print("ARIA — PPO Training — 5 Experiments")
    print(f"Timesteps per experiment: {TOTAL_TIMESTEPS:,}")
    print(f"Total timesteps: {TOTAL_TIMESTEPS * len(PPO_EXPERIMENTS):,}")
    print("=" * 60)

    all_results = []
    best_reward = -np.inf
    best_dir    = None

    for cfg in PPO_EXPERIMENTS:
        result = train_experiment(cfg)
        all_results.append(result)

        er  = result.get("eval_results", {})
        rew = er.get("mean_rewards", [])
        if rew and max(rew) > best_reward:
            best_reward = max(rew)
            best_dir    = result["checkpoint"]

        with open(os.path.join(METRICS_DIR, "all_experiments.json"), "w") as f:
            json.dump(all_results, f, indent=2)

    print("\n" + "=" * 60)
    print("Generating all 5 visualisation plots...")
    print("=" * 60)

    plot_cumulative_rewards(all_results)
    plot_entropy_curves(all_results)
    plot_convergence(all_results)
    plot_hyperparameter_table(all_results)

    gen_results = {}
    if best_dir:
        gen_results = generalisation_test(best_dir)
        plot_generalisation(gen_results)
    else:
        print("  No best checkpoint found — skipping generalisation test")

    print("\n" + "=" * 60)
    print("PPO Training Complete")
    print(f"  Experiments run:  {len(all_results)}")
    print(f"  Best eval reward: {best_reward:.2f}")
    print(f"  Best checkpoint:  {best_dir}")
    print(f"  Plots saved to:   {PLOTS_DIR}")
    print(f"  Metrics saved to: {METRICS_DIR}")
    print("=" * 60)


if __name__ == "__main__":
    run()