"""
env/reward_function.py
======================
Composite three-tier reward function for ARIA.

Tier 1 — Immediate placement reward (every seed drop)
Tier 2 — Delayed germination reward (via growth engine)
Tier 3 — Mission-level rewards (abort, battery, obstacles)
"""

import numpy as np
from configs.config import REWARD, SPECIES, N_SPECIES, MIN_SEED_SPACING, MAX_SLOPE_DEG


class RewardFunction:
    def __init__(self, weights: dict = None, min_seed_spacing: float = None):
        self.w = weights or REWARD.copy()
        self.seeded = set()
        self.species_counts = {i: 0 for i in range(N_SPECIES)}
        # Overridable per-instance for parameter sweeps, defaults to the
        # global config value exactly as before when not specified.
        self.min_seed_spacing = min_seed_spacing if min_seed_spacing is not None else MIN_SEED_SPACING

    def reset(self):
        self.seeded.clear()
        self.species_counts = {i: 0 for i in range(N_SPECIES)}

    # ── Tier 1 ─────────────────────────────────────────────────────
    def placement(self, x, y, species_id, soil, rain, slope_deg,
                  prox, in_protected, is_hover, cover_deployed,
                  is_rainy, is_suitable=False, is_reseeding_target=False):
        if is_hover:
            return -self.w["step_penalty"], {}

        w = self.w
        rain_min  = SPECIES[species_id]["rain_min"]
        rain_ok   = max(0.0, rain - rain_min) / (1.0 - rain_min + 1e-6)
        slope_pen = min(slope_deg / MAX_SLOPE_DEG, 1.0)

        # Spacing. Reverted to the original binary check after two
        # separate attempts at a continuous version (a straight line,
        # then a concave curve) both underperformed it in real training:
        # binary averaged 55.8% seeding efficiency across six zones,
        # continuous versions averaged 42-44% in both attempts. The
        # concave version did fix a real, confirmed problem from the
        # first continuous attempt (near-threshold clustering had become
        # exploitably cheap, and landings for the zone-selector/curriculum
        # experiments spiked to 13-14k as a result; the concave fix
        # brought that back down to a normal 6-7k) -- but efficiency
        # still didn't recover even once that specific issue was closed.
        # That's itself useful evidence that reward curve shape wasn't
        # the dominant lever after all, so this goes back to what's
        # actually been shown to work best, rather than continuing to
        # tune a mechanism two consecutive real runs have underperformed
        # with. Skipped for a genuine reseed: revisiting a known failure
        # is deliberate correction, not something to penalise for being
        # close to itself.
        cluster = 0.0
        if not is_reseeding_target:
            for (px, py) in self.seeded:
                if abs(x-px) + abs(y-py) < self.min_seed_spacing:
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

        # Direct bonus for clearing the exact discrete threshold the
        # pct_suitable_seeded evaluation metric checks -- see config.py's
        # comment on w_suitable_bonus for why this exists.
        suitable_bonus = w["w_suitable_bonus"] if is_suitable else 0.0

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
            + suitable_bonus
        )
        self.seeded.add((x, y))

        breakdown = {
            "soil": w["w_soil"]*soil, "rain": w["w_rain"]*rain_ok,
            "slope": -w["w_slope"]*slope_pen, "cluster": cluster,
            "protected": protected, "disturbance": dist_pen,
            "reseed_bonus": reseed_bon, "cover": cover_r,
            "diversity": div_r, "suitable_bonus": suitable_bonus, "total": total,
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
