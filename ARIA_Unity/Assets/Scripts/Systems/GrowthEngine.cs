using System.Collections.Generic;
using UnityEngine;
using ARIA.Core;

namespace ARIA.Systems
{
    public enum SeedStage { Dropped, Germinating, Seedling, Mature, Dead }

    public class Seed
    {
        public int       SeedId;
        public int       SpeciesId;
        public int       X, Y;
        public int       DroppedAt;        // simulation timestep
        public System.DateTime DroppedAtUtc;  // real wall-clock time (live Unity run)
        public float     SoilScore;
        public float     RainScore;
        public float     SlopeScore;
        public float     CorridorProximity;
        public bool      IsSuitable;
        public bool      InProtected;
        public SeedStage Stage = SeedStage.Dropped;
        public float     SurvivalProb = 1f;
    }

    public class FailedCell
    {
        public int    X, Y;
        public int    SpeciesTried;
        public int    FailedAt;             // simulation timestep
        public System.DateTime FailedAtUtc; // real wall-clock time (live Unity run)
        public string Reason;       // "natural_mortality" or "disturbance"
        public float  Soil;
        public float  Rain;
        public float  Slope;              // NEW -- was computed per-seed but not surfaced before
        public float  CorridorProximity;  // NEW -- ditto

        /* Filled in by MonitoringSystem via the learned SpeciesRecommender */
        public int    RecommendedSpecies;
        public float  Priority;           // = PredictedSurvival, not a separate hardcoded formula
        public float  PredictedSurvival;
        public float[] RecommendFeatures; // kept so a later outcome can update the recommender
    }

    public class GrowthEngine
    {
        private readonly int _zoneSize;
        private readonly System.Random _rng;
        public Dictionary<int, Seed> Seeds = new Dictionary<int, Seed>();
        public List<FailedCell> FailedCells = new List<FailedCell>();
        private int _nextId = 0;

        public GrowthEngine(int zoneSize, System.Random rng = null)
        {
            _zoneSize = zoneSize;
            _rng = rng ?? new System.Random();
        }

        public void Reset()
        {
            Seeds.Clear();
            FailedCells.Clear();
            _nextId = 0;
        }

        public int Register(int speciesId, int x, int y, int timestep,
            float soil, float rain, float slope, float prox,
            bool suitable, bool inProtected)
        {
            var s = new Seed
            {
                SeedId = _nextId,
                SpeciesId = speciesId,
                X = x, Y = y,
                DroppedAt = timestep,
                DroppedAtUtc = System.DateTime.UtcNow,
                SoilScore = soil,
                RainScore = rain,
                SlopeScore = slope,
                CorridorProximity = prox,
                IsSuitable = suitable,
                InProtected = inProtected,
            };
            Seeds[_nextId] = s;
            _nextId++;
            return s.SeedId;
        }

        private static float Sigmoid(float x)
        {
            float v = 1f / (1f + Mathf.Exp(-x));
            return Mathf.Clamp(v, 0.05f, 0.95f);
        }

        /* Returns the (x,y) of every seed that matured THIS call, so
           MonitoringSystem can credit any pending reseed at that position
           with a real success outcome (see MonitoringSystem.ResolveMatured),
           plus the real delayed growth-tier reward for this tick (mirrors
           growth_engine.py's step(): -w_germ*0.5 per natural death,
           +w_germ per maturity). */
        public (List<(int x, int y)> matured, float reward) Step(int timestep, float[,] rainMap)
        {
            var matured = new List<(int x, int y)>();
            float reward = 0f;
            foreach (var kv in new List<KeyValuePair<int, Seed>>(Seeds))
            {
                var s = kv.Value;
                if (s.Stage == SeedStage.Dead || s.Stage == SeedStage.Mature)
                    continue;

                /* Update rain from current season */
                s.RainScore = rainMap[s.Y, s.X];

                int germT   = SpeciesGermSteps(s.SpeciesId);
                int matureT = SpeciesMatureSteps(s.SpeciesId);

                float score = s.SoilScore + s.RainScore - s.SlopeScore - s.CorridorProximity * 0.5f;
                float quality = Sigmoid(score * 2f);
                float targetCumulative = 0.10f + 0.85f * quality;
                s.SurvivalProb = Mathf.Pow(targetCumulative, 1f / Mathf.Max(matureT, 1));

                /* Natural mortality roll */
                if ((float)_rng.NextDouble() > s.SurvivalProb)
                {
                    s.Stage = SeedStage.Dead;
                    reward += -ARIAConstants.REWARD_W_GERM * 0.5f;
                    FailedCells.Add(new FailedCell
                    {
                        X = s.X, Y = s.Y,
                        SpeciesTried = s.SpeciesId,
                        FailedAt = timestep,
                        FailedAtUtc = System.DateTime.UtcNow,
                        Reason = "natural_mortality",
                        Soil = s.SoilScore,
                        Rain = s.RainScore,
                        Slope = s.SlopeScore,
                        CorridorProximity = s.CorridorProximity,
                    });
                    continue;
                }

                int age = timestep - s.DroppedAt;
                int midT = (germT + matureT) / 2;

                if (s.Stage == SeedStage.Dropped && age >= germT)
                {
                    s.Stage = SeedStage.Germinating;
                }
                else if (s.Stage == SeedStage.Germinating && age >= midT)
                {
                    s.Stage = SeedStage.Seedling;
                }
                else if (s.Stage == SeedStage.Seedling && age >= matureT)
                {
                    s.Stage = SeedStage.Mature;
                    reward += ARIAConstants.REWARD_W_GERM;
                    matured.Add((s.X, s.Y));
                }
            }
            return (matured, reward);
        }

        /* Unlike natural mortality in Step(), this can kill a Mature tree too
           (goats) -- a deliberate Unity-only divergence from growth_engine.py's
           kill(), which no-ops on an already-mature seed (Python's living()
           never offers mature seeds to disturbance in the first place). Since
           that specific state is unreachable in the trained environment, there
           is no "real" reward value for it; reusing the same natural-death
           penalty here is the simplest consistent choice, not a parity claim.
           Returns the reward delta (0 if the seed was already dead). */
        public float Kill(int seedId, int timestep, string reason = "disturbance")
        {
            if (!Seeds.TryGetValue(seedId, out var s)) return 0f;
            if (s.Stage == SeedStage.Dead) return 0f;

            s.Stage = SeedStage.Dead;
            FailedCells.Add(new FailedCell
            {
                X = s.X, Y = s.Y,
                SpeciesTried = s.SpeciesId,
                FailedAt = timestep,
                FailedAtUtc = System.DateTime.UtcNow,
                Reason = reason,
                Soil = s.SoilScore,
                Rain = s.RainScore,
                Slope = s.SlopeScore,
                CorridorProximity = s.CorridorProximity,
            });
            return -ARIAConstants.REWARD_W_GERM * 0.5f;
        }

        public float[,] LifecycleMap()
        {
            var m = new float[_zoneSize, _zoneSize];
            foreach (var s in Seeds.Values)
            {
                m[s.Y, s.X] = s.Stage switch
                {
                    SeedStage.Dropped     => 0.0f,
                    SeedStage.Germinating => 0.33f,
                    SeedStage.Seedling    => 0.66f,
                    SeedStage.Mature      => 1.0f,
                    SeedStage.Dead        => -1.0f,
                    _ => 0.0f,
                };
            }
            return m;
        }

        public List<Seed> Living()
        {
            var result = new List<Seed>();
            foreach (var s in Seeds.Values)
                if (s.Stage != SeedStage.Dead && s.Stage != SeedStage.Mature)
                    result.Add(s);
            return result;
        }

        /* Everything not yet dead, including Mature -- used wherever disturbance can threaten trees. */
        public List<Seed> Alive()
        {
            var result = new List<Seed>();
            foreach (var s in Seeds.Values)
                if (s.Stage != SeedStage.Dead)
                    result.Add(s);
            return result;
        }

        /* Deliberately slowed down (roughly 2.5x the original steps) so
           growth reads as a genuine, gradual process rather than trees
           popping up within seconds of being planted -- a demo-pacing
           decision, not a training-parity value. Still comfortably inside
           MAX_STEPS (1800) so a normal-length episode has time to watch a
           seed actually reach Mature, not just Germinating/Seedling. */
        private static int SpeciesGermSteps(int speciesId) => speciesId switch
        {
            0 => 100,  // Eucalyptus globulus  -- fast
            1 => 125,  // Grevillea robusta    -- moderate
            2 => 100,  // Eucalyptus maculata  -- fast
            3 => 115,  // Eucalyptus maidenii  -- fast
            4 => 175,  // Artocarpus heterophyllus -- slow (jackfruit)
            _ => 115,
        };
        private static int SpeciesMatureSteps(int speciesId) => speciesId switch
        {
            0 => 875,
            1 => 1000,
            2 => 925,
            3 => 950,
            4 => 1125,
            _ => 950,
        };
    }
}
