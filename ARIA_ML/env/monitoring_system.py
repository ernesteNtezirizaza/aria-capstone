"""
env/monitoring_system.py
========================
Records all failed seeds and builds a reseeding priority queue.
When called at mission end, recommends which cells to reseed, which
species to try next, and how urgently -- using a learned SpeciesRecommender
(see env/species_recommender.py) rather than hardcoded rules, and closing
the loop with realised outcomes: when a replanted seed later matures or
dies again, that result feeds back into the recommender.
"""

from configs.config import SPECIES
from env.species_recommender import SpeciesRecommender


class MonitoringSystem:
    """
    Tracks failed seeds and schedules reseeding missions.

    For each failed cell, records:
      - Location (x, y)
      - Species tried
      - Failure reason
      - Terrain scores at time of failure (soil, rain, slope, corridor
        proximity -- the same features the reward function scores placement
        on)

    When a reseeding mission is planned, recommends the next species to
    try via a learned SpeciesRecommender, and prioritises the queue by that
    same model's predicted survival score (not a separate hardcoded
    soil+rain formula) -- then, once that reseed either matures or dies
    again, updates the recommender with what actually happened, so both
    "which species" and "how urgent" are genuinely learned rather than
    hand-coded.
    """

    def __init__(self, recommender: SpeciesRecommender = None, epsilon: float = 0.15):
        self.failed_cells  = []    # raw failure records
        self.reseed_queue  = []    # prioritised reseeding targets
        self.reseed_log    = []    # completed reseedings
        self.recommender   = recommender if recommender is not None else SpeciesRecommender()
        self.epsilon        = epsilon
        # (x, y) -> {"features": np.ndarray, "species": int} for reseeds
        # that have been dropped but not yet resolved (matured or died again).
        self.pending_reseeds = {}

    def reset(self):
        pass

    def full_reset(self):
        self.failed_cells.clear()
        self.reseed_queue.clear()
        self.reseed_log.clear()
        self.pending_reseeds.clear()

    def ingest_failures(self, failed_cells: list):
        """Called periodically to add new failures to the queue."""
        for fc in failed_cells:
            key = (fc["x"], fc["y"])

            # If this cell was a pending reseed, its replacement just
            # failed too -- that's a real (negative) outcome for the
            # species we recommended last time. Feed it back before
            # recommending again for this cell.
            pending = self.pending_reseeds.pop(key, None)
            if pending is not None:
                self.recommender.update(pending["features"], outcome=0.0)

            existing = [r for r in self.reseed_queue if (r["x"], r["y"]) == key]
            if not existing:
                species_id, feats, predicted_survival = self._recommend(fc)
                fc["recommended_species"] = species_id
                fc["_recommend_features"] = feats
                fc["predicted_survival"]  = predicted_survival
                # Priority IS the recommender's predicted survival score for
                # the species it just picked here, not a separate hardcoded
                # formula. "Which cell to reseed first" and "will the species
                # we'd plant there survive" are the same underlying question,
                # so they now share one learned number instead of two
                # disconnected ones (soil+rain average vs. the recommender).
                fc["priority"] = predicted_survival
                self.reseed_queue.append(fc)
            self.failed_cells.append(fc)

        self.reseed_queue.sort(key=lambda x: x["priority"], reverse=True)

    def resolve_matured(self, matured_positions: list):
        """
        Called once per monitoring interval with the (x, y) of every seed
        that matured this step. Any position that was a pending reseed is
        a real success outcome for the species we recommended -- feed it
        back into the recommender.
        """
        for (x, y) in matured_positions:
            pending = self.pending_reseeds.pop((x, y), None)
            if pending is not None:
                self.recommender.update(pending["features"], outcome=1.0)

    def _recommend(self, fc: dict):
        """
        Recommend next species to try at a failed cell using the learned
        SpeciesRecommender (soil, rain, slope, corridor proximity, failure
        reason, and each candidate species' own rain requirement / growth
        speed), instead of a fixed if/else rule. Also returns the
        recommender's own predicted survival score for that pick, which
        doubles as this cell's reseed priority (see ingest_failures).
        """
        cell = {
            "soil":               fc.get("soil", 0.5),
            "rain":               fc.get("rain", 0.5),
            "slope_pen":          fc.get("slope", 0.0),
            "corridor_proximity": fc.get("corridor_proximity", 0.0),
            "reason":             fc.get("reason", "natural_mortality"),
        }
        tried = fc.get("species_tried", None)
        species_id, feats, score = self.recommender.recommend(
            cell, SPECIES, epsilon=self.epsilon, exclude=tried,
            return_features=True, return_score=True,
        )
        return species_id, feats, score

    def get_top_targets(self, n: int = 5) -> list:
        """Return top N reseeding targets."""
        return self.reseed_queue[:n]

    def mark_reseeded(self, x: int, y: int):
        """
        Called when the drone actually drops a seed on a queued reseed
        target. Moves that target's recommendation (species + features)
        into pending_reseeds so the outcome can be attributed back to the
        recommender once it resolves (see ingest_failures / resolve_matured).
        """
        match = next((r for r in self.reseed_queue
                      if r["x"] == x and r["y"] == y), None)
        if match is not None and "_recommend_features" in match:
            self.pending_reseeds[(x, y)] = {
                "features": match["_recommend_features"],
                "species":  match.get("recommended_species"),
            }
            # Diagnostic: this is the drone actually reaching and replanting
            # a queued target, separate from whether that replant later
            # resolves (see SpeciesRecommender.reseed_attempts docstring).
            self.recommender.reseed_attempts += 1

        self.reseed_queue = [
            r for r in self.reseed_queue
            if not (r["x"] == x and r["y"] == y)
        ]
        self.reseed_log.append({"x": x, "y": y})

    def queue_size(self) -> int:
        return len(self.reseed_queue)

    def summary(self) -> dict:
        return {
            "total_failures":    len(self.failed_cells),
            "pending_reseeds":   len(self.reseed_queue),
            "completed_reseeds": len(self.reseed_log),
            "recommender_updates": self.recommender.n_updates,
        }
