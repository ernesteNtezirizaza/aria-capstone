"""
utils/nine_zone_variance.py
============================
Evaluates the best trained policy (ppo_exp_01) across all 9 zones actually
shipped in the ARIA_Unity demo (the 6 held-out eval zones already covered
by generalisation.csv, plus the 3 sampled train zones referenced in
ARIA_Unity/Assets/StreamingAssets/zone_manifest.json), 50 episodes each,
to confirm terrain/seed/reward outcomes genuinely vary with real
geospatial differences across the full demo zone set, not just the
held-out subset.

Run: python utils/nine_zone_variance.py
"""

import csv
import os
import sys

import numpy as np
from stable_baselines3 import PPO
from stable_baselines3.common.env_util import make_vec_env
from stable_baselines3.common.monitor import Monitor

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from configs.config import METRICS_DIR, N_EVAL_EPISODES, ZONE_DEFINITIONS, CHECKPOINTS_DIR
from env.rwanda_env import RwandaReforestEnv
from training.train_ppo import SHARED_SPECIES_RECOMMENDER

# Matches ARIA_Unity/Assets/StreamingAssets/zone_manifest.json exactly --
# the 3 train-split zones actually shipped in the demo, by array_index
# within the train split (same convention as ARIA_ML/utils/export_zone.py).
TRAIN_ZONES_IN_DEMO = [0, 17, 29]


def run_zone(model, split, zone_id, display_name, n_episodes):
    env = Monitor(RwandaReforestEnv(zone_id=zone_id, split=split, seed=42,
                                     species_recommender=SHARED_SPECIES_RECOMMENDER))
    ep_metrics = []
    for _ in range(n_episodes):
        obs, _ = env.reset()
        done = False
        info = {}
        while not done:
            action, _ = model.predict(obs, deterministic=False)
            obs, _, terminated, truncated, info = env.step(int(action))
            done = terminated or truncated
        if "episode_metrics" in info:
            ep_metrics.append(info["episode_metrics"])
    env.close()

    if not ep_metrics:
        print(f"  {display_name}: no metrics collected, skipping")
        return None

    return {
        k: round(float(np.mean([m.get(k, 0.0) for m in ep_metrics])), 4)
        for k in ep_metrics[0]
    }


def run():
    best_path = os.path.join(CHECKPOINTS_DIR, "ppo_exp_01", "best_model")
    print(f"Loading best model: {best_path}")
    dummy_env = make_vec_env(lambda: RwandaReforestEnv(split="eval"), n_envs=1)
    model = PPO.load(best_path, env=dummy_env)
    dummy_env.close()

    rows = []

    print(f"\n=== Train-split zones shipped in the demo ({N_EVAL_EPISODES} episodes each) ===")
    train_zone_defs = [z for z in ZONE_DEFINITIONS if z[5] == "train"]
    for zone_id in TRAIN_ZONES_IN_DEMO:
        display = train_zone_defs[zone_id][3] if zone_id < len(train_zone_defs) else str(zone_id)
        print(f"  Testing train zone {zone_id} ({display})...", end=" ", flush=True)
        metrics = run_zone(model, "train", zone_id, display, N_EVAL_EPISODES)
        if metrics:
            metrics["zone"] = f"train_{zone_id}_{display.replace(' ', '_')}"
            metrics["split"] = "train"
            rows.append(metrics)
            print(f"pct_suitable_seeded = {metrics.get('pct_suitable_seeded', 0):.3f}")

    # Fold in the already-computed eval-zone results so this file is a
    # single, complete 9-zone record, not just the 3 new train zones.
    gen_csv = os.path.join(METRICS_DIR, "generalisation.csv")
    if os.path.exists(gen_csv):
        with open(gen_csv) as f:
            for row in csv.DictReader(f):
                row["split"] = "eval"
                rows.append(row)

    out_path = os.path.join(METRICS_DIR, "nine_zone_variance.csv")
    if rows:
        fieldnames = list(rows[0].keys())
        with open(out_path, "w", newline="") as f:
            w = csv.DictWriter(f, fieldnames=fieldnames)
            w.writeheader()
            for row in rows:
                w.writerow(row)
        print(f"\nWrote {len(rows)} zone results to {out_path}")


if __name__ == "__main__":
    run()
