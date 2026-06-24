"""
env/disturbance_engine.py
=========================
Animal disturbance near protected area boundaries.
"""

import numpy as np
from configs.config import DISTURBANCE_BASE_PROB, REWARD


class DisturbanceEngine:
    def __init__(self, rng=None):
        self.rng    = rng or np.random.default_rng()
        self.events = []

    def reset(self):
        self.events.clear()

    def step(self, growth_engine, timestep):
        events, reward = [], 0.0
        for seed in growth_engine.living():
            p = DISTURBANCE_BASE_PROB * seed.corridor_proximity
            if p > 0 and self.rng.random() < p:
                penalty = growth_engine.kill(
                    seed.seed_id, timestep, "disturbance"
                )
                extra   = -REWARD.get("w_disturbance", 0.6)
                reward += penalty + extra
                e = {
                    "seed_id":   seed.seed_id,
                    "x":         seed.x,
                    "y":         seed.y,
                    "timestep":  timestep,
                    "proximity": seed.corridor_proximity,
                }
                events.append(e)
                self.events.append(e)
        return events, reward

    def get_disturbance_summary(self):
        return {
            "total_disturbance_events": len(self.events),
            "seeds_destroyed":          len(self.events),
        }