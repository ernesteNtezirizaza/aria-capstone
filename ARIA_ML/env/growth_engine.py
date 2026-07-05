"""
env/growth_engine.py
====================
Tracks seed lifecycle: dropped → germinating → seedling → mature → dead.
Fires delayed rewards when seeds mature.
Records all failed seeds for the monitoring system.
"""

import numpy as np
from dataclasses import dataclass, field
from typing import Dict, List, Optional, Tuple
from configs.config import SPECIES, MONITORING_INTERVAL, REWARD, ZONE_SIZE


@dataclass
class Seed:
    seed_id:           int
    species_id:        int
    x: int;            y: int
    dropped_at:        int
    soil_score:        float
    rain_score:        float
    slope_score:       float
    corridor_proximity: float
    is_suitable:       bool
    in_protected:      bool
    stage:             str = "dropped"
    survival_prob:     float = 1.0


def sigmoid(x):
    return float(np.clip(1.0 / (1.0 + np.exp(-x)), 0.05, 0.95))


class GrowthEngine:
    def __init__(self, zone_size=ZONE_SIZE, rng=None):
        self.zone_size = zone_size
        self.rng       = rng or np.random.default_rng()
        self.seeds: Dict[int, Seed] = {}
        self.events: List[dict]     = []
        self.failed_cells: List[dict] = []   # for monitoring/reseeding
        self._nid = 0

    def reset(self):
        self.seeds.clear()
        self.events.clear()
        self.failed_cells.clear()
        self._nid = 0

    def register(self, species_id, x, y, timestep,
                 soil, rain, slope, prox, suitable, protected):
        s = Seed(
            seed_id=self._nid, species_id=species_id,
            x=x, y=y, dropped_at=timestep,
            soil_score=soil, rain_score=rain,
            slope_score=slope, corridor_proximity=prox,
            is_suitable=suitable, in_protected=protected,
        )
        self.seeds[self._nid] = s
        self._nid += 1
        return s.seed_id

    def step(self, timestep, rain_map) -> Tuple[List[dict], float]:
        events, reward = [], 0.0
        for sid, s in list(self.seeds.items()):
            if s.stage in ("dead", "mature"):
                continue

            # Update rain from current season
            s.rain_score = float(rain_map[s.y, s.x])

            # Survival probability — calibrated so a high-quality seed
            mature_t   = SPECIES[s.species_id]["mature_steps"]
            score      = s.soil_score + s.rain_score - s.slope_score - s.corridor_proximity * 0.5
            quality    = sigmoid(score * 2.0)

            target_cumulative = 0.10 + 0.85 * quality
            s.survival_prob   = float(target_cumulative ** (1.0 / max(mature_t, 1)))

            # Natural mortality
            if self.rng.random() > s.survival_prob:
                s.stage = "dead"
                r = -REWARD["w_germ"] * 0.5
                reward += r
                events.append({"type": "natural_death", "seed_id": sid,
                               "x": s.x, "y": s.y, "species": s.species_id})
                self.failed_cells.append({
                    "x": s.x, "y": s.y,
                    "species_tried": s.species_id,
                    "failed_at": timestep,
                    "reason": "natural_mortality",
                    "soil": s.soil_score,
                    "rain": s.rain_score,
                })
                continue

            age = timestep - s.dropped_at
            germ_t   = SPECIES[s.species_id]["germ_steps"]
            mature_t = SPECIES[s.species_id]["mature_steps"]
            mid_t    = (germ_t + mature_t) // 2

            if s.stage == "dropped" and age >= germ_t:
                s.stage = "germinating"
                events.append({"type": "germination", "seed_id": sid})
            elif s.stage == "germinating" and age >= mid_t:
                s.stage = "seedling"
                events.append({"type": "seedling", "seed_id": sid})
            elif s.stage == "seedling" and age >= mature_t:
                s.stage = "mature"
                reward += REWARD["w_germ"]
                events.append({"type": "mature", "seed_id": sid,
                               "x": s.x, "y": s.y})

        self.events.extend(events)
        return events, reward

    def kill(self, seed_id, timestep, reason="disturbance"):
        if seed_id not in self.seeds:
            return 0.0
        s = self.seeds[seed_id]
        if s.stage in ("dead", "mature"):
            return 0.0
        s.stage = "dead"
        self.failed_cells.append({
            "x": s.x, "y": s.y,
            "species_tried": s.species_id,
            "failed_at": timestep,
            "reason": reason,
            "soil": s.soil_score,
            "rain": s.rain_score,
        })
        return -REWARD["w_germ"] * 0.5

    def lifecycle_map(self):
        stage_v = {"dropped": 0.0, "germinating": 0.33,
                   "seedling": 0.66, "mature": 1.0, "dead": -1.0}
        m = np.zeros((self.zone_size, self.zone_size), dtype=np.float32)
        for s in self.seeds.values():
            m[s.y, s.x] = stage_v.get(s.stage, 0.0)
        return m

    def living(self):
        return [s for s in self.seeds.values()
                if s.stage not in ("dead", "mature")]

    def summary(self):
        all_s = list(self.seeds.values())
        if not all_s:
            return {"total": 0, "mature": 0, "dead": 0,
                    "reseeding_count": 0, "maturity_rate": 0.0}
        counts = {st: sum(1 for s in all_s if s.stage == st)
                  for st in ("dropped","germinating","seedling","mature","dead")}
        dead_pos  = {(s.x,s.y) for s in all_s if s.stage == "dead"}
        alive_pos = {(s.x,s.y) for s in all_s if s.stage != "dead"}
        return {
            "total":          len(all_s),
            "mature":         counts["mature"],
            "dead":           counts["dead"],
            "reseeding_count": len(dead_pos & alive_pos),
            "maturity_rate":  counts["mature"] / len(all_s),
        }
