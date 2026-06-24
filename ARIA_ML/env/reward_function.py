"""
env/reward_function.py
======================
Composite three-tier reward function for ARIA V2.

Tier 1 — Immediate placement reward (every seed drop)
Tier 2 — Delayed germination reward (via growth engine)
Tier 3 — Mission-level rewards (abort, battery, obstacles)
"""

import numpy as np
from configs.config import REWARD, SPECIES, N_SPECIES, MIN_SEED_SPACING


class RewardFunction:
    def __init__(self, weights: dict = None):
        self.w = weights or REWARD.copy()
        self.seeded = set()
        self.species_counts = {i: 0 for i in range(N_SPECIES)}

    def reset(self):
        self.seeded.clear()
        self.species_counts = {i: 0 for i in range(N_SPECIES)}

    # ── Tier 1 ─────────────────────────────────────────────────────
    def placement(self, x, y, species_id, soil, rain, slope_deg,
                  prox, in_protected, is_hover, cover_deployed,
                  is_rainy, is_reseeding_target=False):
        if is_hover:
            return -self.w["step_penalty"], {}

        w = self.w
        rain_min  = SPECIES[species_id]["rain_min"]
        rain_ok   = max(0.0, rain - rain_min) / (1.0 - rain_min + 1e-6)
        slope_pen = min(slope_deg / 30.0, 1.0)

        # Spacing
        cluster = 0.0
        for (px, py) in self.seeded:
            if abs(x-px) + abs(y-py) < MIN_SEED_SPACING:
                cluster = -w["w_spacing"]
                break

        protected  = -w["w_protected"] if in_protected else 0.0
        dist_pen   = -w["w_disturbance"] * prox
        reseed_bon = w["w_reseed"] if is_reseeding_target else 0.0

        # Rain cover correctness
        cover_r = 0.0
        if is_rainy and cover_deployed:
            cover_r = w["cover_correct"]
        elif is_rainy and not cover_deployed:
            cover_r = w["cover_wrong"]
        elif not is_rainy and cover_deployed:
            cover_r = w["cover_wrong"]

        # Diversity
        self.species_counts[species_id] += 1
        div_r = self._diversity()

        total = (
            w["w_soil"] * soil
            + w["w_rain"] * rain_ok
            - w["w_slope"] * slope_pen
            + cluster
            + protected
            + dist_pen
            + reseed_bon
            + cover_r
            + div_r
            - w["step_penalty"]
        )
        self.seeded.add((x, y))

        breakdown = {
            "soil": w["w_soil"]*soil, "rain": w["w_rain"]*rain_ok,
            "slope": -w["w_slope"]*slope_pen, "cluster": cluster,
            "protected": protected, "disturbance": dist_pen,
            "reseed_bonus": reseed_bon, "cover": cover_r,
            "diversity": div_r, "total": total,
        }
        return float(total), breakdown

    def _diversity(self) -> float:
        total = sum(self.species_counts.values())
        if total == 0:
            return 0.0
        probs = [c/total for c in self.species_counts.values() if c > 0]
        H = -sum(p * np.log(p) for p in probs)
        return float(self.w["w_diversity"] * H / np.log(N_SPECIES))

    # ── Tier 3 — Mission-level ──────────────────────────────────────
    def battery_save(self):
        return float(self.w["battery_save"])

    def bad_abort(self):
        return float(self.w["bad_abort"])

    def battery_empty(self):
        return float(self.w["battery_empty"])

    def obstacle_clear(self):
        return float(self.w["obstacle_clear"])

    def obstacle_hit(self):
        return float(self.w["obstacle_hit"])

    def hover_penalty(self):
        return float(-self.w["step_penalty"])
