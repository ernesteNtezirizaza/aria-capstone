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
                penalty = growth_engine.kill(seed.seed_id, timestep, "disturbance")
                extra   = -REWARD["w_disturbance"]
                reward += penalty + extra
                e = {"seed_id": seed.seed_id, "x": seed.x, "y": seed.y,
                     "timestep": timestep, "proximity": seed.corridor_proximity}
                events.append(e)
                self.events.append(e)
        return events, reward

    def summary(self):
        return {
            "total_disturbance_events": len(self.events),
            "seeds_destroyed":          len(self.events),
        }


"""
env/monitoring_system.py
========================
Records all failed seeds and builds a reseeding priority queue.
When called at mission end, recommends which cells to reseed
and which species to try next based on failure analysis.
"""


class MonitoringSystem:
    """
    Tracks failed seeds and schedules reseeding missions.

    For each failed cell, records:
      - Location (x, y)
      - Species tried
      - Failure reason
      - Terrain scores at time of failure

    When a reseeding mission is planned, recommends the next
    best species based on soil and rainfall at that cell.
    """

    def __init__(self):
        self.failed_cells  = []    # raw failure records
        self.reseed_queue  = []    # prioritised reseeding targets
        self.reseed_log    = []    # completed reseedings

    def reset(self):
        # NOTE: do NOT clear between episodes
        # monitoring persists across missions
        pass

    def full_reset(self):
        self.failed_cells.clear()
        self.reseed_queue.clear()
        self.reseed_log.clear()

    def ingest_failures(self, failed_cells: list):
        """Called at episode end to add new failures to the queue."""
        for fc in failed_cells:
            # Avoid duplicates
            key = (fc["x"], fc["y"])
            existing = [r for r in self.reseed_queue
                        if (r["x"], r["y"]) == key]
            if not existing:
                fc["recommended_species"] = self._recommend(fc)
                fc["priority"] = self._priority(fc)
                self.reseed_queue.append(fc)
            self.failed_cells.append(fc)

        # Sort by priority (highest first)
        self.reseed_queue.sort(key=lambda x: x["priority"], reverse=True)

    def _recommend(self, fc: dict) -> int:
        """
        Recommend next species to try at a failed cell.

        Logic:
          If failure was due to low rain → try drought-tolerant species (0)
          If failure was due to disturbance → try fast-growing species (0)
          If failure was natural mortality → try next species up
        """
        from configs.config import SPECIES
        tried = fc.get("species_tried", 0)
        reason = fc.get("reason", "natural_mortality")
        rain   = fc.get("rain", 0.5)

        if reason == "disturbance" or rain < 0.35:
            return 0   # Eucalyptus grandis — most resilient
        # Try next species in the list
        return min(tried + 1, len(SPECIES) - 1)

    def _priority(self, fc: dict) -> float:
        """
        Higher priority = reseed sooner.
        Good soil + good rain = high priority (likely to succeed).
        """
        soil = fc.get("soil", 0.5)
        rain = fc.get("rain", 0.5)
        return (soil + rain) / 2.0

    def get_top_targets(self, n: int = 5) -> list:
        """Return top N reseeding targets."""
        return self.reseed_queue[:n]

    def mark_reseeded(self, x: int, y: int):
        """Remove cell from queue when drone reseeds it."""
        self.reseed_queue = [
            r for r in self.reseed_queue
            if not (r["x"] == x and r["y"] == y)
        ]
        self.reseed_log.append({"x": x, "y": y})

    def queue_size(self) -> int:
        return len(self.reseed_queue)

    def summary(self) -> dict:
        return {
            "total_failures":   len(self.failed_cells),
            "pending_reseeds":  len(self.reseed_queue),
            "completed_reseeds": len(self.reseed_log),
        }
