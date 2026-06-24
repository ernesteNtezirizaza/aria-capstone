"""
main.py
=======
ARIA — Single entry point.

Usage:
  python main.py --step preprocess
  python main.py --step zones
  python main.py --step validate
  python main.py --step train
  python main.py --step all
"""

import argparse, os, sys


def preprocess():
    print("\n── STEP 1: PREPROCESSING ───────────────────────────────")
    from utils.preprocess import run
    run()


def build_zones():
    print("\n── STEP 2: ZONE BUILDING ───────────────────────────────")
    from utils.zone_builder import run
    run()


def validate():
    print("\n── STEP 3: ENVIRONMENT VALIDATION ─────────────────────")
    from gymnasium.utils.env_checker import check_env
    from env.rwanda_env import RwandaReforestEnv
    print("  Creating RwandaReforestEnv(zone_id=1, split='train')...")
    env = RwandaReforestEnv(zone_id=1, split="train", seed=42)
    print("  Running check_env()...")
    check_env(env)
    print("  Environment passed all gymnasium checks")
    env.close()


def train():
    print("\n── STEP 4: PPO TRAINING (10 experiments) ───────────────")
    from training.train_ppo import run
    run()


def main():
    parser = argparse.ArgumentParser(description="ARIA V2 ML Pipeline")
    parser.add_argument(
        "--step",
        choices=["preprocess", "zones", "validate", "train", "all"],
        default="all"
    )
    args = parser.parse_args()

    print("╔═══════════════════════════════════════════════════════╗")
    print("║  ARIA — Adaptive Reforestation Intelligence Agent  ║")
    print("╚═══════════════════════════════════════════════════════╝")

    if args.step == "all":
        preprocess()
        build_zones()
        validate()
        train()
    elif args.step == "preprocess":  preprocess()
    elif args.step == "zones":       build_zones()
    elif args.step == "validate":    validate()
    elif args.step == "train":       train()

    print("\nDone.")


if __name__ == "__main__":
    main()
