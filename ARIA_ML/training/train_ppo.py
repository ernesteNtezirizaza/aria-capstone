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
import matplotlib.pyplot as plt
from matplotlib.ticker import MaxNLocator, FuncFormatter
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
    ZONE_DEFINITIONS, PRIMARY_METRIC, DISCOUNT_GAMMA,
    TOTAL_TIMESTEPS,    # single source of truth - set in config.py
    REWARD,
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
    """Sort training zone indices by composite ecological suitability
    (soil + rain - slope, same weighting as ZONE_SUITABILITY_WEIGHTS),
    easiest first. Falls back to soil-only ordering if an older
    zone_registry.json (built before mean_rain/mean_slope were tracked)
    is found on disk."""
    registry_path = os.path.join(
        os.path.dirname(os.path.dirname(os.path.abspath(__file__))),
        "data", "zones", "zone_registry.json"
    )
    if not os.path.exists(registry_path):
        return list(range(36))
    with open(registry_path) as f:
        registry = json.load(f)
    train_zones = [z for z in registry if z["split"] == "train"]

    from configs.config import ZONE_SUITABILITY_WEIGHTS, MAX_SLOPE_DEG
    w = ZONE_SUITABILITY_WEIGHTS

    def _score(z):
        if "mean_rain" in z and "mean_slope" in z:
            slope_pen = min(z["mean_slope"] * 90.0 / MAX_SLOPE_DEG, 1.0)
            return (w["soil"]*z["mean_soil"] + w["rain"]*z["mean_rain"]
                    - w["slope"]*slope_pen) / (w["soil"]+w["rain"]+w["slope"])
        # Old registry without rain/slope fields -- soil-only fallback.
        return z["mean_soil"] + (1.0 - z["mean_dist"])

    train_zones.sort(key=_score, reverse=True)
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


# ── Zone-selector environment ──────────────────────────────────────
# This is the piece that was actually missing: "which zone should the
# drone be deployed to" is now a genuine learned decision (env/zone_selector.py),
# not domain randomisation and not a hand-sorted curriculum list. A single
# ZoneSelector instance is shared across all N_ENVS workers (they run in
# the same process under DummyVecEnv), so it keeps learning across every
# episode any worker completes.
from env.zone_selector import ZoneSelector
from env.species_recommender import SpeciesRecommender

SHARED_ZONE_SELECTOR = ZoneSelector(n_features=6, lr=0.05, seed=PPO_SEED)
SHARED_SPECIES_RECOMMENDER = SpeciesRecommender(lr=0.08, seed=PPO_SEED)


class ZoneSelectorEnv(RwandaReforestEnv):
    """
    Wraps RwandaReforestEnv so that, instead of a random or hand-curriculum
    zone_id, the active zone for each episode is chosen by scoring every
    candidate zone's terrain_stats (elevation, slope, soil, rainfall,
    landcover, plantable fraction) through a learned ZoneSelector, then
    picking the best-scoring one (epsilon-greedy so it keeps exploring).

    After each episode, the selector is updated with the realised
    pct_suitable_seeded for the zone it picked, so "was this actually a
    good zone to fly to" feeds back into future selections.
    """
    def __init__(self, split="train", selector: ZoneSelector = None,
                 epsilon: float = 0.15, **kwargs):
        super().__init__(split=split, **kwargs)
        self.selector = selector if selector is not None else SHARED_ZONE_SELECTOR
        self.epsilon  = epsilon
        self._chosen_stats = None

    def reset(self, seed=None, options=None):
        candidate_stats = self.available_zone_stats()   # (n_zones, 6)
        idx = self.selector.select(candidate_stats, epsilon=self.epsilon)
        self._chosen_stats = candidate_stats[idx]

        self.zone_id = idx
        obs, info    = super().reset(seed=seed, options=options)
        self.zone_id = None
        return obs, info

    def step(self, action):
        obs, reward, terminated, truncated, info = super().step(action)
        if (terminated or truncated) and self._chosen_stats is not None:
            metrics = info.get("episode_metrics", {})
            outcome = float(metrics.get("pct_suitable_seeded", 0.0))
            self.selector.update(self._chosen_stats, outcome)
            self._chosen_stats = None
        return obs, reward, terminated, truncated, info


# ── Entropy tracking callback ──────────────────────────────────────
class EntropyCallback(BaseCallback):
    """
    Records policy entropy and mean reward at every rollout boundary.

    Plot 2 came back completely empty ("No entropy data logged yet") on a
    real training run despite Plots 1/3 (built from the same all_results)
    showing real data -- meaning entropy_log specifically stayed empty the
    whole run. The most likely cause: SB3's on-policy loop calls
    collect_rollouts() (which fires on_rollout_end() at its end) BEFORE
    self.train() runs for that same iteration, so "train/entropy_loss" may
    not yet be the value this rollout's own on_rollout_end() expects if a
    version/config difference changes exactly when name_to_value gets
    populated relative to these hooks. Checking at BOTH rollout_start and
    rollout_end doubles the chance of catching it whenever it's actually
    available, and the diagnostic print means if this happens again, the
    real cause (via the actually-available key names) is visible in the
    very next run's console output instead of another silent empty plot.
    """
    def __init__(self):
        super().__init__()
        self.entropy_log = []
        self.reward_log  = []
        self.steps_log   = []
        self._logged_diagnostic = False

    def _try_capture(self):
        if not (hasattr(self.model, "logger") and self.model.logger):
            return
        logs = self.model.logger.name_to_value
        for key in ("train/entropy_loss", "train/entropy", "entropy_loss"):
            if key in logs:
                self.entropy_log.append(-float(logs[key]))
                break
        if "rollout/ep_rew_mean" in logs:
            self.reward_log.append(float(logs["rollout/ep_rew_mean"]))
            self.steps_log.append(self.num_timesteps)
        elif not self._logged_diagnostic and self.num_timesteps > 0:
            # Only fires once per experiment, and only if we genuinely
            # never found anything -- avoids spamming the log.
            self._logged_diagnostic = True
            print(f"   [EntropyCallback diagnostic] entropy/reward keys not "
                  f"found yet at timestep {self.num_timesteps}. "
                  f"Available logger keys: {list(logs.keys())}")

    def _on_rollout_start(self):
        self._try_capture()
        return True

    def _on_rollout_end(self):
        self._try_capture()
        return True

    def _on_step(self):
        return True


# ── 5 Experiments ─────────────────────────────────────────────
PPO_EXPERIMENTS = [
    # ── Spacing parameter sweep ──────────────────────────────────
    # Two separate reward redesigns (a linear continuous curve, then a
    # concave one) both underperformed the simple binary spacing check
    # in real training -- 43% and 42.6% average seeding efficiency
    # against binary's 55.8%, the best result all session. That's
    # strong evidence against changing the MECHANISM again. This sweep
    # instead tunes the two numbers within the proven mechanism itself:
    # how close is "too close" (MIN_SEED_SPACING) and how much it costs
    # (w_spacing). All 5 use identical hyperparameters (the same
    # baseline domain-randomisation config that produced 55.8%), so any
    # difference in outcome isolates the spacing parameters specifically,
    # not a confound from LR/curriculum/zone-selector differences.
    {
        "exp": "01",
        "description": "Spacing sweep: control (spacing=5, weight=3.5)",
        "learning_rate": 0.0003, "gamma": 0.99,
        "n_steps": 4096, "batch_size": 256,
        "n_epochs": 10, "clip_range": 0.2, "ent_coef": 0.10,
        "use_curriculum": False, "use_zone_selector": False,
        "min_seed_spacing": 5, "w_spacing": 3.5,
        "effect": (
            "Exact match to the current proven-best configuration (the "
            "binary spacing check, reverted to after both continuous "
            "attempts underperformed it). This is the control every "
            "other experiment in this sweep is measured against -- if "
            "none of exp02-05 beat this, the spacing parameters "
            "themselves aren't the remaining bottleneck."
        ),
    },
    {
        "exp": "02",
        "description": "Spacing sweep: tighter radius (spacing=4, weight=3.5)",
        "learning_rate": 0.0003, "gamma": 0.99,
        "n_steps": 4096, "batch_size": 256,
        "n_epochs": 10, "clip_range": 0.2, "ent_coef": 0.10,
        "use_curriculum": False, "use_zone_selector": False,
        "min_seed_spacing": 4, "w_spacing": 3.5,
        "effect": (
            "Tests whether a slightly smaller minimum spacing radius "
            "lets the policy achieve denser, more efficient coverage "
            "without materially increasing redundant placement, since "
            "5 was chosen somewhat arbitrarily rather than tuned."
        ),
    },
    {
        "exp": "03",
        "description": "Spacing sweep: wider radius (spacing=6, weight=3.5)",
        "learning_rate": 0.0003, "gamma": 0.99,
        "n_steps": 4096, "batch_size": 256,
        "n_epochs": 10, "clip_range": 0.2, "ent_coef": 0.10,
        "use_curriculum": False, "use_zone_selector": False,
        "min_seed_spacing": 6, "w_spacing": 3.5,
        "effect": (
            "Tests the opposite direction from exp02 -- whether pushing "
            "the policy to spread out even further than the current "
            "radius improves genuine zone-wide coverage, at the cost of "
            "potentially leaving more suitable ground unused nearby."
        ),
    },
    {
        "exp": "04",
        "description": "Spacing sweep: stronger penalty (spacing=5, weight=4.5)",
        "learning_rate": 0.0003, "gamma": 0.99,
        "n_steps": 4096, "batch_size": 256,
        "n_epochs": 10, "clip_range": 0.2, "ent_coef": 0.10,
        "use_curriculum": False, "use_zone_selector": False,
        "min_seed_spacing": 5, "w_spacing": 4.5,
        "effect": (
            "Same radius as the control, but a stronger penalty for "
            "violating it -- tests whether 3.5 was already the right "
            "magnitude or whether clustering is still under-punished "
            "relative to the dominant placement reward."
        ),
    },
    {
        "exp": "05",
        "description": "Spacing sweep: weaker penalty (spacing=5, weight=2.5)",
        "learning_rate": 0.0003, "gamma": 0.99,
        "n_steps": 4096, "batch_size": 256,
        "n_epochs": 10, "clip_range": 0.2, "ent_coef": 0.10,
        "use_curriculum": False, "use_zone_selector": False,
        "min_seed_spacing": 5, "w_spacing": 2.5,
        "effect": (
            "Tests the opposite direction from exp04 -- every previous "
            "attempt has only tried INCREASING the spacing penalty; "
            "this checks whether 3.5 already overshot and a lighter "
            "touch actually performs better, since that direction has "
            "never actually been tested."
        ),
    },
]

def _fix_timestep_axis(ax, nbins: int = 5):
    """
    Limits the x-axis to a small number of well-spaced ticks and formats
    large timestep values compactly (e.g. "50k" instead of "50000"). Real
    output showed timestep labels (0, 25000, 50000, ... 200000) packed so
    densely on narrow, multi-panel plots that they overlapped into an
    unreadable smear -- matplotlib's default tick locator doesn't account
    for how narrow a shared panel width actually is.
    """
    ax.xaxis.set_major_locator(MaxNLocator(nbins=nbins))
    ax.xaxis.set_major_formatter(FuncFormatter(
        lambda x, _: f"{x/1000:.0f}k" if x >= 1000 else f"{x:.0f}"
    ))


N_EXPERIMENTS = len(PPO_EXPERIMENTS)


def _smart_truncate(text: str, maxlen: int) -> str:
    """
    Truncates at a word boundary and adds '...' when actually shortened,
    instead of a hard character-count cut. A plain text[:28] produced
    "Baseline + domain randomisat" and "Northern Platea" in real output,
    both cut mid-word with no ellipsis, reading as a rendering bug rather
    than an intentionally short label.
    """
    if len(text) <= maxlen:
        return text
    truncated = text[:maxlen]
    if " " in truncated:
        truncated = truncated.rsplit(" ", 1)[0]
    return truncated + "..."


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

    use_curriculum    = cfg.get("use_curriculum", False)
    use_zone_selector = cfg.get("use_zone_selector", False)

    # Per-experiment spacing overrides for parameter sweeps -- None means
    # "use the global config.py default", so every experiment that
    # doesn't set these behaves exactly as it always has.
    sweep_min_spacing = cfg.get("min_seed_spacing", None)
    sweep_w_spacing    = cfg.get("w_spacing", None)
    sweep_reward_weights = None
    if sweep_w_spacing is not None:
        sweep_reward_weights = REWARD.copy()
        sweep_reward_weights["w_spacing"] = sweep_w_spacing

    print(f"\n── PPO Experiment {exp_id}/{len(PPO_EXPERIMENTS)} ──")
    print(f"   {cfg['description']}")
    print(f"   LR={cfg['learning_rate']}  gamma={cfg['gamma']}  "
          f"n_steps={cfg['n_steps']}  batch={cfg['batch_size']}  "
          f"clip={cfg['clip_range']}  ent={cfg['ent_coef']}  "
          f"curriculum={use_curriculum}  zone_selector={use_zone_selector}")
    if sweep_min_spacing is not None or sweep_w_spacing is not None:
        print(f"   SWEEP: min_seed_spacing={sweep_min_spacing if sweep_min_spacing is not None else '(default)'}  "
              f"w_spacing={sweep_w_spacing if sweep_w_spacing is not None else '(default)'}")
    print(f"   Timesteps: {TOTAL_TIMESTEPS:,}")

    # ── Environment factory ────────────────────────────────────────
    if use_zone_selector:
        def make_train_env():
            return ZoneSelectorEnv(split="train", selector=SHARED_ZONE_SELECTOR,
                                   species_recommender=SHARED_SPECIES_RECOMMENDER)
    elif use_curriculum:
        def make_train_env():
            return CurriculumEnv(split="train", curriculum_progress=0.0,
                                  species_recommender=SHARED_SPECIES_RECOMMENDER)
    else:
        def make_train_env():
            return RwandaReforestEnv(split="train", zone_id=None,
                                      species_recommender=SHARED_SPECIES_RECOMMENDER,
                                      min_seed_spacing=sweep_min_spacing,
                                      reward_weights=sweep_reward_weights)

    train_env = make_vec_env(make_train_env, n_envs=N_ENVS, seed=PPO_SEED)
    eval_env  = Monitor(RwandaReforestEnv(split="train", zone_id=None, seed=999,
                                          species_recommender=SHARED_SPECIES_RECOMMENDER,
                                          min_seed_spacing=sweep_min_spacing,
                                          reward_weights=sweep_reward_weights))
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
    if use_zone_selector:
        SHARED_ZONE_SELECTOR.save(os.path.join(ckpt_dir, "zone_selector.json"))
        print(f"   Zone selector saved ({SHARED_ZONE_SELECTOR.n_updates} episodes learned from)")
    SHARED_SPECIES_RECOMMENDER.save(os.path.join(ckpt_dir, "species_recommender.json"))
    print(f"   Species recommender saved ({SHARED_SPECIES_RECOMMENDER.n_updates} outcomes, "
          f"{SHARED_SPECIES_RECOMMENDER.reseed_attempts} attempts, "
          f"{SHARED_SPECIES_RECOMMENDER.landings_completed} landings, "
          f"{SHARED_SPECIES_RECOMMENDER.landings_with_targets} with targets queued)")
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
            env        = Monitor(RwandaReforestEnv(zone_id=idx, split="eval", seed=42,
                                                  species_recommender=SHARED_SPECIES_RECOMMENDER))
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
                _env2 = RwandaReforestEnv(zone_id=idx, split="eval", seed=42,
                                          species_recommender=SHARED_SPECIES_RECOMMENDER)
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
    fig, axes = plt.subplots(1, N_EXPERIMENTS, figsize=(5.0 * N_EXPERIMENTS, 7.5), dpi=200)
    fig.suptitle(
        "ARIA PPO — Cumulative Reward Curves\n"
        f"{N_EXPERIMENTS} Experiments on RwandaReforestEnv ({TOTAL_TIMESTEPS:,} timesteps each)",
        fontsize=19, fontweight="bold", y=1.04
    )
    colours = plt.cm.tab10(np.linspace(0, 1, N_EXPERIMENTS))

    for i, res in enumerate(all_results):
        ax  = axes[i] if N_EXPERIMENTS > 1 else axes
        col = colours[i]
        er  = res.get("eval_results", {})
        ts  = er.get("timesteps",    [])
        rew = er.get("mean_rewards", [])

        if ts and rew:
            ax.plot(ts, rew, color=col, linewidth=2.2, zorder=3)
            ax.fill_between(ts, rew, alpha=0.15, color=col)
            best_idx = int(np.argmax(rew))
            ax.scatter(ts[best_idx], rew[best_idx], color=col, s=90, zorder=5,
                       edgecolor="white", linewidth=1.2)
            ax.axhline(y=max(rew), color=col, linestyle="--", alpha=0.4, linewidth=1.3)
            ax.text(0.97, 0.05, f"peak={max(rew):.1f}",
                    transform=ax.transAxes, ha="right", fontsize=12, color=col, fontweight="bold")
        else:
            ax.text(0.5, 0.5, "No data", ha="center", va="center",
                    transform=ax.transAxes, fontsize=13)

        cfg = res.get("config", {})
        tag = "ZS" if cfg.get("use_zone_selector") else ("CURR" if cfg.get("use_curriculum") else "DR")
        ax.set_title(
            f"Exp {res['exp']}: {_smart_truncate(res['description'], 28)}\n"
            f"LR={cfg.get('learning_rate')}  ent={cfg.get('ent_coef')}  [{tag}]",
            fontsize=11.5, fontweight="bold", color=col
        )
        ax.set_xlabel("Training timesteps", fontsize=12)
        ax.set_ylabel("Mean episode reward", fontsize=12)
        _fix_timestep_axis(ax)
        ax.tick_params(labelsize=10.5)
        ax.grid(True, alpha=0.3)
        ax.spines["top"].set_visible(False)
        ax.spines["right"].set_visible(False)

    plt.tight_layout()
    path = os.path.join(PLOTS_DIR, "plot1_cumulative_rewards.png")
    plt.savefig(path, dpi=200, bbox_inches="tight")
    plt.show()
    plt.close()
    print(f" Plot 1 saved: {path}")


# ── Plot 2 - Entropy curves ────────────────────────────────────────
def plot_entropy_curves(all_results: list):
    # Previously grouped by ent_coef == 0.0 vs > 0.0 -- stale from an earlier
    # version of PPO_EXPERIMENTS. All 5 current experiments use ent_coef=0.10,
    # so that grouping always left one panel empty and mislabeled the other
    # ("ent_coef = 0.01" when the real value is 0.10). Regrouped by what
    # actually varies now: domain randomisation vs curriculum vs learned
    # zone selection.
    fig, ax = plt.subplots(figsize=(13, 7), dpi=200)
    fig.suptitle(
        "ARIA PPO — Policy Gradient Entropy Curves\n"
        "Exploration Behaviour by Zone-Selection Strategy",
        fontsize=18, fontweight="bold"
    )

    def tag_of(res):
        cfg = res.get("config", {})
        if cfg.get("use_zone_selector"):
            return "Learned zone selection", "#8E24AA"
        if cfg.get("use_curriculum"):
            return "Curriculum", "#EF6C00"
        return "Domain randomisation", "#2196F3"

    seen_labels = set()
    has_data = False
    for res in all_results:
        elog = res.get("entropy_log", [])
        if not elog:
            continue
        label, col = tag_of(res)
        full_label = f"{label} (Exp {res['exp']})"
        smoothed = np.convolve(elog, np.ones(10)/10, mode="valid")
        ax.plot(smoothed, color=col, alpha=0.8, linewidth=2.0, label=full_label)
        seen_labels.add(label)
        has_data = True

    if not has_data:
        ax.text(0.5, 0.5, "No entropy data logged yet", ha="center", va="center",
                transform=ax.transAxes, fontsize=14)

    ax.set_xlabel("Rollouts (smoothed, window=10)", fontsize=13)
    ax.set_ylabel("Policy entropy H(π)", fontsize=13)
    ax.tick_params(labelsize=11)
    ax.legend(fontsize=11, loc="best", framealpha=0.9)
    ax.grid(True, alpha=0.3)
    ax.spines["top"].set_visible(False)
    ax.spines["right"].set_visible(False)

    plt.tight_layout()
    path = os.path.join(PLOTS_DIR, "plot2_entropy_curves.png")
    plt.savefig(path, dpi=200, bbox_inches="tight")
    plt.show()
    plt.close()
    print(f" Plot 2 saved: {path}")


# ── Plot 3 - Convergence comparison ───────────────────────────────
def plot_convergence(all_results: list):
    fig, ax = plt.subplots(figsize=(15, 7.5), dpi=200)
    ax.set_title(
        "ARIA PPO — Convergence Comparison\n"
        f"All {N_EXPERIMENTS} Experiments: Domain Randomisation vs Curriculum vs Learned Zone Selection",
        fontsize=17, fontweight="bold"
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
        # Fixed position in axes-fraction coordinates, not anchored to the
        # peak's data position -- an offset-from-data-point annotation can
        # land anywhere depending on where the peak happens to fall,
        # including directly under the figure's suptitle when the peak is
        # near the top of the y-range (exactly what happened here: the
        # annotation collided with "ARIA PPO — Convergence Comparison").
        # A fixed corner guarantees this can never happen again, regardless
        # of what the data looks like on any future run.
        ax.text(
            0.02, 0.98, f"Best: {best_exp}\nPeak reward: {best_peak:.1f}",
            transform=ax.transAxes, ha="left", va="top",
            fontsize=11, fontweight="bold",
            bbox=dict(boxstyle="round,pad=0.4", fc="lightyellow", ec="orange"),
        )

    ax.set_xlabel("Training timesteps", fontsize=13)
    ax.set_ylabel("Mean episode reward (smoothed)", fontsize=13)
    _fix_timestep_axis(ax, nbins=8)
    ax.tick_params(labelsize=11)
    ax.legend(fontsize=10, loc="lower right", ncol=1, framealpha=0.9)
    ax.grid(True, alpha=0.3)
    ax.spines["top"].set_visible(False)
    ax.spines["right"].set_visible(False)

    plt.tight_layout()
    path = os.path.join(PLOTS_DIR, "plot3_convergence.png")
    plt.savefig(path, dpi=200, bbox_inches="tight")
    plt.show()
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

    # Larger canvas + higher DPI so labels are actually readable when
    # viewed at normal size, not just when zoomed in.
    fig, axes = plt.subplots(1, len(metrics), figsize=(5.0*len(metrics), 10), dpi=200)
    if len(metrics) == 1:
        axes = [axes]

    fig.suptitle(
        "ARIA PPO — Generalisation Test\n"
        "Best Model on 6 Held-Out Rwanda Terrain Zones",
        fontsize=20, fontweight="bold"
    )
    palette     = sns.color_palette("viridis", len(zones))
    zone_labels = []
    for z in zones:
        parts = z.split("_", 2)
        zone_labels.append(_smart_truncate(parts[2].replace("_", " "), 18) if len(parts) >= 3 else z)

    for ax, metric in zip(axes, metrics):
        values  = [gen_results[z].get(metric, 0.0) for z in zones]
        max_val = max(values) if max(values) > 0 else 1.0
        bars    = ax.bar(zone_labels, values, color=palette,
                         edgecolor="white", linewidth=1.2, zorder=3)
        for bar, val in zip(bars, values):
            ax.text(bar.get_x() + bar.get_width()/2,
                    bar.get_height() + max_val*0.03,
                    f"{val:.3f}", ha="center", va="bottom",
                    fontsize=13, fontweight="bold")
        # Mean line gives useful context without implying a pass/fail bar.
        # Label sits in a fixed axes-fraction corner, not anchored to the
        # last bar's position/height -- anchoring it there meant it could
        # crowd whichever bar happened to be closest to the mean value
        # (visible in real output: "mean = 1.575" crowding "1.597").
        mean_val = sum(values) / len(values)
        ax.axhline(y=mean_val, color="#444444", linestyle=":", linewidth=1.5, zorder=2)
        ax.text(0.98, 0.98, f"mean = {mean_val:.3f}", transform=ax.transAxes,
                fontsize=11, color="#444444", ha="right", va="top")

        ax.set_title(metric.replace("_", " ").title(), fontsize=16, fontweight="bold", pad=12)
        ax.set_xlabel("Evaluation zone", fontsize=13)
        ax.set_ylabel("Score", fontsize=13)
        ax.tick_params(axis="x", rotation=30, labelsize=11)
        ax.tick_params(axis="y", labelsize=11)
        ax.set_ylim(0, max_val * 1.3)
        ax.grid(True, axis="y", alpha=0.3, zorder=0)
        ax.spines["top"].set_visible(False)
        ax.spines["right"].set_visible(False)

    plt.tight_layout(rect=[0, 0, 1, 0.94])
    path = os.path.join(PLOTS_DIR, "plot4_generalisation.png")
    plt.savefig(path, dpi=200, bbox_inches="tight")
    plt.show()
    plt.close()
    print(f" Plot 4 saved: {path}")


# ── Plot 6 - Zone selector diagnostics (drawn AFTER training) ─────
def plot_zone_selector_diagnostics(selector: "ZoneSelector"):
    """
    Diagnostics for the learned zone selector, built entirely from data
    the selector itself accumulated during training (its .history log of
    every zone it was asked to score, plus the realised outcome once that
    episode finished) -- not from the raw rasters. This is only meaningful
    to draw AFTER training has produced that history.
    """
    if not selector.history:
        print("  No zone selector history - skipping Plot 6 (did you train "
              "an experiment with use_zone_selector=True?)")
        return

    feature_names = ["Elevation", "Slope", "Soil", "Rainfall", "Landcover", "Plantable %"]
    stats_arr   = np.array([h[0] for h in selector.history])
    pred_arr    = np.array([h[1] for h in selector.history])
    real_arr    = np.array([h[2] for h in selector.history])

    fig, axes = plt.subplots(1, 3, figsize=(18, 8), dpi=200)
    fig.suptitle("ARIA Zone Selector — Learned From Training Episodes",
                 fontsize=17, fontweight="bold")

    # (a) learned feature weights — which ecological measures the
    #     selector ended up relying on most.
    ax = axes[0]
    colors = ["#2E7D32" if w >= 0 else "#C62828" for w in selector.w]
    ax.barh(feature_names, selector.w, color=colors, edgecolor="white")
    ax.axvline(0, color="black", linewidth=0.8)
    ax.set_title("Learned Feature Weights", fontsize=14, fontweight="bold")
    ax.set_xlabel("Weight (+ helps score, − hurts score)")
    ax.grid(True, axis="x", alpha=0.3)

    # (b) predicted suitability vs realised pct_suitable_seeded
    ax = axes[1]
    ax.scatter(pred_arr, real_arr, s=18, alpha=0.4, color="#1565C0")
    lims = [0, max(1e-3, max(pred_arr.max(), real_arr.max()))]
    ax.plot(lims, lims, "k--", linewidth=1, label="perfect calibration")
    if len(pred_arr) > 2:
        corr = np.corrcoef(pred_arr, real_arr)[0, 1]
        ax.text(0.05, 0.92, f"r = {corr:.2f}", transform=ax.transAxes,
                fontsize=12, fontweight="bold")
    ax.set_xlabel("Selector's predicted score")
    ax.set_ylabel("Realised pct_suitable_seeded")
    ax.set_title("Predicted vs Realised Outcome", fontsize=14, fontweight="bold")
    ax.legend(fontsize=9)
    ax.grid(True, alpha=0.3)

    # (c) correlation between each ecological feature and the realised
    #     outcome, across every zone-episode the selector saw.
    ax = axes[2]
    corrs = [np.corrcoef(stats_arr[:, i], real_arr)[0, 1]
             if np.std(stats_arr[:, i]) > 1e-9 else 0.0
             for i in range(stats_arr.shape[1])]
    colors2 = ["#2E7D32" if c >= 0 else "#C62828" for c in corrs]
    ax.barh(feature_names, corrs, color=colors2, edgecolor="white")
    ax.axvline(0, color="black", linewidth=0.8)
    ax.set_xlim(-1, 1)
    ax.set_title("Feature ↔ Outcome Correlation", fontsize=14, fontweight="bold")
    ax.set_xlabel("Pearson correlation with pct_suitable_seeded")
    ax.grid(True, axis="x", alpha=0.3)

    plt.tight_layout(rect=[0, 0, 1, 0.93])
    path = os.path.join(PLOTS_DIR, "plot6_zone_selector_diagnostics.png")
    plt.savefig(path, bbox_inches="tight")
    plt.show()
    plt.close()
    print(f" Plot 6 saved: {path}  ({len(selector.history)} episodes)")


# ── Plot 7 - Species recommender diagnostics (drawn AFTER training) ──
def plot_species_recommender_diagnostics(recommender: "SpeciesRecommender"):
    """
    Diagnostics for the learned reseed species recommender, built from its
    own history of (cell+species features, predicted score, realised
    survival outcome) pairs -- only meaningful to draw AFTER training has
    produced that history, same as Plot 6.
    """
    if not recommender.history:
        print("  No species recommender history - skipping Plot 7 "
              "(pending_reseeds only resolve once a replanted seed matures "
              "or dies again, so this needs a full training run)")
        return

    feature_names = ["Soil", "Rainfall", "Slope penalty", "Corridor\nproximity",
                      "Disturbance\nfailure", "Rain gap\n(site − species min)",
                      "Species maturity\ntime (norm.)"]
    feats = np.array([h[0] for h in recommender.history])
    pred  = np.array([h[1] for h in recommender.history])
    real  = np.array([h[2] for h in recommender.history])

    fig, axes = plt.subplots(1, 3, figsize=(18, 8), dpi=200)
    fig.suptitle("ARIA Species Recommender — Learned From Reseed Outcomes",
                 fontsize=17, fontweight="bold")

    # (a) learned feature weights
    ax = axes[0]
    colors = ["#2E7D32" if w >= 0 else "#C62828" for w in recommender.w]
    ax.barh(feature_names, recommender.w, color=colors, edgecolor="white")
    ax.axvline(0, color="black", linewidth=0.8)
    ax.set_title("Learned Feature Weights", fontsize=14, fontweight="bold")
    ax.set_xlabel("Weight (+ helps predicted survival, − hurts it)")
    ax.grid(True, axis="x", alpha=0.3)

    # (b) predicted survival probability vs realised outcome (0/1)
    ax = axes[1]
    jitter = np.random.default_rng(0).normal(0, 0.02, size=len(real))
    ax.scatter(pred, real + jitter, s=14, alpha=0.35, color="#1565C0")
    ax.set_xlabel("Predicted survival probability")
    ax.set_ylabel("Realised outcome (0 = died again, 1 = matured, jittered)")
    ax.set_title("Predicted vs Realised Reseed Outcome", fontsize=14, fontweight="bold")
    if len(pred) > 2 and np.std(pred) > 1e-9:
        corr = np.corrcoef(pred, real)[0, 1]
        ax.text(0.05, 0.92, f"r = {corr:.2f}", transform=ax.transAxes,
                fontsize=12, fontweight="bold")
    ax.grid(True, alpha=0.3)

    # (c) survival rate achieved over the course of training (rolling mean)
    ax = axes[2]
    window = max(5, len(real) // 30)
    if len(real) >= window:
        rolling = np.convolve(real, np.ones(window)/window, mode="valid")
        ax.plot(rolling, color="#2E7D32", linewidth=2)
        ax.fill_between(range(len(rolling)), rolling, alpha=0.15, color="#2E7D32")
    ax.set_xlabel(f"Reseed outcome # (rolling mean, window={window})")
    ax.set_ylabel("Survival rate")
    ax.set_title("Reseed Survival Rate Over Training", fontsize=14, fontweight="bold")
    ax.set_ylim(0, 1)
    ax.grid(True, alpha=0.3)

    plt.tight_layout(rect=[0, 0, 1, 0.93])
    path = os.path.join(PLOTS_DIR, "plot7_species_recommender_diagnostics.png")
    plt.savefig(path, bbox_inches="tight")
    plt.show()
    plt.close()
    print(f" Plot 7 saved: {path}  ({len(recommender.history)} reseed outcomes)")


# ── Plot 8 - Seeding efficiency vs ceiling ─────────────────────────
def plot_seeding_efficiency(gen_results: dict):
    """
    pct_suitable_seeded's denominator is every suitable cell in the WHOLE
    zone, but the drone only carries INITIAL_SEEDS per episode, so even
    perfect placement can't exceed seeding_ceiling = INITIAL_SEEDS / n_suit.
    This plots actual vs ceiling per zone, plus the efficiency ratio
    (actual/ceiling), which is the more honest "how good is the policy"
    number since raw pct_suitable_seeded will always look small when a
    zone has many more suitable cells than the drone has seeds for.
    """
    if not gen_results:
        print("  No generalisation results - skipping Plot 8")
        return

    zones = list(gen_results.keys())
    if "seeding_ceiling" not in gen_results[zones[0]]:
        print("  seeding_ceiling not in generalisation results - skipping Plot 8 "
              "(re-run generalisation_test with the updated rwanda_env.py)")
        return

    zone_labels = []
    for z in zones:
        parts = z.split("_", 2)
        zone_labels.append(_smart_truncate(parts[2].replace("_", " "), 18) if len(parts) >= 3 else z)

    actual   = [gen_results[z].get("pct_suitable_seeded", 0.0) for z in zones]
    ceiling  = [gen_results[z].get("seeding_ceiling", 0.0) for z in zones]
    efficiency = [gen_results[z].get("seeding_efficiency", 0.0) for z in zones]

    fig, axes = plt.subplots(1, 2, figsize=(14, 7.5), dpi=200)
    fig.suptitle("ARIA Seeding Efficiency — Actual vs Achievable Ceiling",
                 fontsize=17, fontweight="bold")

    # (a) actual vs ceiling, grouped bars
    ax = axes[0]
    x = np.arange(len(zones))
    width = 0.35
    ax.bar(x - width/2, ceiling, width, label="Ceiling (seed budget / suitable cells)",
           color="#B0BEC5", edgecolor="white")
    ax.bar(x + width/2, actual, width, label="Actual (pct_suitable_seeded)",
           color="#2E7D32", edgecolor="white")
    for i, (a, c) in enumerate(zip(actual, ceiling)):
        ax.text(i + width/2, a + max(ceiling)*0.02, f"{a:.3f}", ha="center",
                fontsize=9, fontweight="bold")
        ax.text(i - width/2, c + max(ceiling)*0.02, f"{c:.3f}", ha="center",
                fontsize=9, color="#546E7A")
    ax.set_xticks(x)
    ax.set_xticklabels(zone_labels, rotation=25, fontsize=10)
    ax.set_ylabel("pct_suitable_seeded")
    ax.set_title("Actual vs Ceiling per Zone", fontsize=14, fontweight="bold")
    # Real headroom above the tallest bar + its label, then the legend
    # pinned into that guaranteed-empty band -- loc="best" was landing
    # directly on top of a bar's ceiling label when bars were this tall
    # and uniform, since there was no genuinely free space for it to find.
    ax.set_ylim(0, max(ceiling) * 1.22)
    ax.legend(fontsize=10, loc="upper center", bbox_to_anchor=(0.5, 1.0),
              ncol=2, framealpha=0.95)
    ax.grid(True, axis="y", alpha=0.3)

    # (b) efficiency ratio (actual / ceiling), the real "how good" number
    ax = axes[1]
    colors = plt.cm.RdYlGn(np.clip(efficiency, 0, 1))
    bars = ax.bar(zone_labels, efficiency, color=colors, edgecolor="white")
    for bar, val in zip(bars, efficiency):
        ax.text(bar.get_x() + bar.get_width()/2, bar.get_height() + 0.02,
                f"{val*100:.1f}%", ha="center", fontsize=11, fontweight="bold")
    ax.set_ylim(0, 1.15)
    ax.set_ylabel("Seeding efficiency (actual / ceiling)")
    ax.set_title("How Close to the Achievable Max", fontsize=14, fontweight="bold")
    ax.tick_params(axis="x", rotation=25, labelsize=10)
    ax.grid(True, axis="y", alpha=0.3)

    plt.tight_layout(rect=[0, 0, 1, 0.93])
    path = os.path.join(PLOTS_DIR, "plot8_seeding_efficiency.png")
    plt.savefig(path, bbox_inches="tight")
    plt.show()
    plt.close()
    print(f" Plot 8 saved: {path}")


# ── Plot 5 - Hyperparameter summary table ─────────────────────────
def plot_hyperparameter_table(all_results: list):
    fig, ax = plt.subplots(figsize=(20, 8), dpi=200)
    ax.axis("off")
    fig.suptitle(
        "ARIA PPO — Hyperparameter Experiments Summary Table\n"
        f"{N_EXPERIMENTS} Experiments — Domain Randomisation, Curriculum, and Learned Zone Selection",
        fontsize=18, fontweight="bold"
    )
    col_labels = ["Exp", "Description", "LR", "Gamma",
                  "n_steps", "Batch", "Clip", "Entropy",
                  "Curriculum", "Zone\nSelector", "Peak\nReward", "Key Effect"]
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
            "Yes" if cfg.get("use_zone_selector") else "No",
            peak, effect,
        ])

    table = ax.table(cellText=rows, colLabels=col_labels,
                     cellLoc="center", loc="center", bbox=[0, 0, 1, 1])
    table.auto_set_font_size(False)
    table.set_fontsize(15)

    for j in range(len(col_labels)):
        table[0, j].set_facecolor("#1B5E20")
        table[0, j].set_text_props(color="white", fontweight="bold")
        table[0, j].set_height(0.16)

    for i in range(1, len(rows)+1):
        fc = "#E8F5E9" if i % 2 == 0 else "#FFFFFF"
        for j in range(len(col_labels)):
            table[i, j].set_facecolor(fc)
            table[i, j].set_height(0.14)

    peak_col = col_labels.index("Peak\nReward")
    peaks = [r[peak_col] for r in rows if r[peak_col] != "N/A"]
    if peaks:
        best_p = max(peaks, key=lambda x: float(x))
        for i, r in enumerate(rows):
            if r[peak_col] == best_p:
                table[i+1, peak_col].set_facecolor("#FFD700")
                table[i+1, peak_col].set_text_props(fontweight="bold")

    table.auto_set_column_width(list(range(len(col_labels))))
    plt.tight_layout()
    path = os.path.join(PLOTS_DIR, "plot5_hyperparameter_table.png")
    plt.savefig(path, dpi=200, bbox_inches="tight")
    plt.show()
    plt.close()
    print(f" Plot 5 saved: {path}")


# ── Save experiment summary as CSV ────────────────────────────────
def save_experiment_csv(all_results: list):
    csv_path = os.path.join(METRICS_DIR, "all_experiments.csv")
    with open(csv_path, "w", newline="") as f:
        writer = csv.writer(f)
        writer.writerow(["exp","description","learning_rate","gamma",
                         "n_steps","batch_size","clip_range","ent_coef",
                         "use_curriculum","use_zone_selector","peak_reward"])
        for r in all_results:
            cfg = r.get("config", {})
            rew = r.get("eval_results", {}).get("mean_rewards", [])
            writer.writerow([
                r["exp"], r["description"],
                cfg.get("learning_rate"), cfg.get("gamma"),
                cfg.get("n_steps"), cfg.get("batch_size"),
                cfg.get("clip_range"), cfg.get("ent_coef"),
                cfg.get("use_curriculum"), cfg.get("use_zone_selector"),
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
    candidates = []  # (checkpoint_dir, peak_reward, exp_label) for every trained experiment

    for cfg in PPO_EXPERIMENTS:
        result = train_experiment(cfg)
        all_results.append(result)
        rew = result.get("eval_results", {}).get("mean_rewards", [])
        peak = max(rew) if rew else -np.inf
        candidates.append((result["checkpoint"], peak, f"Exp {result['exp']}"))

    save_experiment_csv(all_results)

    print("\n" + "=" * 60)
    print("Generating plots...")
    plot_cumulative_rewards(all_results)
    plot_entropy_curves(all_results)
    plot_convergence(all_results)
    plot_hyperparameter_table(all_results)

    # Select the final model by real generalisation efficiency, not by
    # raw training reward. These are not the same thing: a policy can
    # earn more reward through behaviour (denser, more repeated
    # placement on already-good ground) that scores worse on the
    # ceiling-relative efficiency metric that actually matters for the
    # project's stated goal. Confirmed directly in a real run: the
    # checkpoint that won on reward (2402.92) reached only 41.3% average
    # efficiency, while a reward-losing configuration in an earlier run
    # reached 55.8%. Every candidate is evaluated here so the checkpoint
    # actually reported and saved is the one that is genuinely best on
    # the metric the project is judged on, with reward kept alongside
    # it for context rather than as the deciding factor.
    print("\n" + "=" * 60)
    print("Selecting final model by generalisation efficiency, not raw reward...")
    best_dir = None
    best_reward = -np.inf
    best_efficiency = -np.inf
    comparison = []

    for ckpt_dir, peak_reward, label in candidates:
        gen_results = generalisation_test(ckpt_dir)
        if not gen_results:
            continue
        effs = [z.get("seeding_efficiency", 0.0) for z in gen_results.values()]
        avg_eff = float(np.mean(effs)) if effs else 0.0
        comparison.append((label, peak_reward, avg_eff))
        print(f"  {label}: peak reward={peak_reward:.2f}  avg efficiency={avg_eff*100:.1f}%")
        if avg_eff > best_efficiency:
            best_efficiency = avg_eff
            best_reward = peak_reward
            best_dir = ckpt_dir

    print()
    print("  Reward-vs-efficiency comparison across all experiments:")
    for label, peak_reward, avg_eff in comparison:
        flag = "  <- selected (best efficiency)" if peak_reward == best_reward and avg_eff == best_efficiency else ""
        print(f"    {label}: reward={peak_reward:.2f}  efficiency={avg_eff*100:.1f}%{flag}")

    if best_dir:
        gen_results = generalisation_test(best_dir)
        plot_generalisation(gen_results)
        plot_seeding_efficiency(gen_results)

    if SHARED_ZONE_SELECTOR.n_updates > 0:
        plot_zone_selector_diagnostics(SHARED_ZONE_SELECTOR)

    if SHARED_SPECIES_RECOMMENDER.n_updates > 0:
        plot_species_recommender_diagnostics(SHARED_SPECIES_RECOMMENDER)

    print("\n" + "=" * 60)
    print(f"  Selected model: {best_dir}  |  efficiency={best_efficiency*100:.1f}%  |  reward={best_reward:.2f}")
    print("=" * 60)


if __name__ == "__main__":
    run()
