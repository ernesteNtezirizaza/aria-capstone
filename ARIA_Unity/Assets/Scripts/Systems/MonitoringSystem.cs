using System.Collections.Generic;
using System.Linq;
using ARIA.Core;

namespace ARIA.Systems
{
    /// <summary>
    /// Ported from env/monitoring_system.py (Python training side). Tracks
    /// failed seeds and schedules reseeding missions using a learned
    /// SpeciesRecommender rather than the previous hardcoded rules:
    ///   species  : "species 0 if disturbed/dry, else species_tried+1"
    ///   priority : "(soil + rain) / 2", ignoring slope and corridor risk
    /// Both "which species" and "how urgent" now come from the same
    /// learned score, and both close a real feedback loop: when a
    /// replanted seed later matures or dies again, that outcome updates
    /// the recommender (see ResolveMatured / IngestFailures).
    /// </summary>
    public class MonitoringSystem
    {
        public List<FailedCell> FailedCellsLog = new List<FailedCell>();
        public List<FailedCell> ReseedQueue     = new List<FailedCell>();
        public List<(int x, int y)> ReseedLog   = new List<(int, int)>();

        public SpeciesRecommender Recommender;
        public float Epsilon = 0.15f;

        // (x, y) -> pending reseed info, mirroring Python's pending_reseeds:
        // set in MarkReseeded, resolved in either IngestFailures (failed
        // again) or ResolveMatured (matured).
        private readonly Dictionary<(int, int), (float[] Features, int Species)> _pendingReseeds
            = new Dictionary<(int, int), (float[], int)>();

        public MonitoringSystem(SpeciesRecommender recommender = null)
        {
            Recommender = recommender ?? new SpeciesRecommender();
        }

        public void Reset() { /* no-op -- monitoring persists across episodes */ }

        public void FullReset()
        {
            FailedCellsLog.Clear();
            ReseedQueue.Clear();
            ReseedLog.Clear();
            _pendingReseeds.Clear();
        }

        public void IngestFailures(List<FailedCell> failedCells)
        {
            foreach (var fc in failedCells)
            {
                var key = (fc.X, fc.Y);

                // If this cell was a pending reseed, its replacement just
                // failed too -- a real negative outcome for whichever
                // species we recommended last time. Feed it back before
                // recommending again for this cell.
                if (_pendingReseeds.TryGetValue(key, out var pending))
                {
                    Recommender.Update(pending.Features, 0f);
                    _pendingReseeds.Remove(key);
                }

                bool exists = ReseedQueue.Any(r => r.X == fc.X && r.Y == fc.Y);
                if (!exists)
                {
                    bool isDisturbance = fc.Reason == "disturbance";
                    int recommended = Recommender.Recommend(
                        fc.Soil, fc.Rain, fc.Slope, fc.CorridorProximity, isDisturbance,
                        ARIAConstants.SPECIES_RAIN_MIN,
                        SpeciesMatureStepsNorm,
                        ARIAConstants.N_SPECIES,
                        fc.SpeciesTried, Epsilon,
                        out var feats, out var score);

                    fc.RecommendedSpecies = recommended;
                    fc.PredictedSurvival  = score;
                    // Priority IS the recommender's predicted survival score,
                    // not a separate (soil+rain)/2 formula -- "which cell to
                    // reseed first" and "will the species we'd plant there
                    // survive" are the same underlying question.
                    fc.Priority          = score;
                    fc.RecommendFeatures = feats;
                    ReseedQueue.Add(fc);
                }
                FailedCellsLog.Add(fc);
            }

            ReseedQueue = ReseedQueue.OrderByDescending(r => r.Priority).ToList();
        }

        /// <summary>
        /// Called once per monitoring interval with the (x, y) of every
        /// seed that matured this step (see GrowthEngine.Step's return
        /// value). Any position that was a pending reseed is a real
        /// success outcome for the species we recommended -- feed it back.
        /// </summary>
        public void ResolveMatured(List<(int x, int y)> maturedPositions)
        {
            foreach (var pos in maturedPositions)
            {
                if (_pendingReseeds.TryGetValue(pos, out var pending))
                {
                    Recommender.Update(pending.Features, 1f);
                    _pendingReseeds.Remove(pos);
                }
            }
        }

        // Mirrors GrowthEngine's per-species mature-step lookup, normalised
        // the same way Python's SpeciesRecommender._make_features does (/150).
        private static float SpeciesMatureStepsNorm(int speciesId)
        {
            int steps = speciesId switch
            {
                0 => 350, 1 => 400, 2 => 370, 3 => 380, 4 => 450, _ => 380,
            };
            return System.Math.Min(steps / 150f, 1f);
        }

        public List<FailedCell> GetTopTargets(int n = 5)
        {
            return ReseedQueue.Take(n).ToList();
        }

        /// <summary>
        /// Called when the drone actually drops a seed on a queued reseed
        /// target. Moves that target's recommendation (species + features)
        /// into _pendingReseeds so the outcome can be attributed back to
        /// the recommender once it resolves (see IngestFailures / ResolveMatured).
        /// </summary>
        public void MarkReseeded(int x, int y)
        {
            var match = ReseedQueue.FirstOrDefault(r => r.X == x && r.Y == y);
            if (match != null && match.RecommendFeatures != null)
            {
                _pendingReseeds[(x, y)] = (match.RecommendFeatures, match.RecommendedSpecies);
            }

            ReseedQueue.RemoveAll(r => r.X == x && r.Y == y);
            ReseedLog.Add((x, y));
        }

        public int QueueSize() => ReseedQueue.Count;
    }
}
