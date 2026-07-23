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

        // Filled in by MonitoringSystem via the learned SpeciesRecommender
        public int    RecommendedSpecies;
        public float  Priority;           // = PredictedSurvival, not a separate hardcoded formula
        public float  PredictedSurvival;
        public float[] RecommendFeatures; // kept so a later outcome can update the recommender
    }

    public struct GrowthStepResult
    {
        public float Reward; // delayed maturity/death reward (NOT used for visuals, kept for parity)
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

        // Returns the (x,y) of every seed that matured THIS call, so
        // MonitoringSystem can credit any pending reseed at that position
        // with a real success outcome (see MonitoringSystem.ResolveMatured).
        public List<(int x, int y)> Step(int timestep, float[,] rainMap)
        {
            var matured = new List<(int x, int y)>();
            foreach (var kv in new List<KeyValuePair<int, Seed>>(Seeds))
            {
                var s = kv.Value;
                if (s.Stage == SeedStage.Dead || s.Stage == SeedStage.Mature)
                    continue;

                // Update rain from current season
                s.RainScore = rainMap[s.Y, s.X];

                int germT   = SpeciesGermSteps(s.SpeciesId);
                int matureT = SpeciesMatureSteps(s.SpeciesId);

                float score = s.SoilScore + s.RainScore - s.SlopeScore - s.CorridorProximity * 0.5f;
                float quality = Sigmoid(score * 2f);
                float targetCumulative = 0.10f + 0.85f * quality;
                s.SurvivalProb = Mathf.Pow(targetCumulative, 1f / Mathf.Max(matureT, 1));

                // Natural mortality roll
                if ((float)_rng.NextDouble() > s.SurvivalProb)
                {
                    s.Stage = SeedStage.Dead;
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
                    matured.Add((s.X, s.Y));
                }
            }
            return matured;
        }

        // Unlike natural mortality in Step(), this can kill a Mature tree too (goats).
        public void Kill(int seedId, int timestep, string reason = "disturbance")
        {
            if (!Seeds.TryGetValue(seedId, out var s)) return;
            if (s.Stage == SeedStage.Dead) return;

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

        // Everything not yet dead, including Mature -- used wherever disturbance can threaten trees.
        public List<Seed> Alive()
        {
            var result = new List<Seed>();
            foreach (var s in Seeds.Values)
                if (s.Stage != SeedStage.Dead)
                    result.Add(s);
            return result;
        }

        // Episodes can end well before MAX_STEPS once fully planted, so growth must reach
        // Mature with time to spare, not just within the full step budget.
        private static int SpeciesGermSteps(int speciesId) => speciesId switch
        {
            0 => 40,  // Eucalyptus globulus  -- fast
            1 => 50,  // Grevillea robusta    -- moderate
            2 => 40,  // Eucalyptus maculata  -- fast
            3 => 45,  // Eucalyptus maidenii  -- fast
            4 => 70,  // Artocarpus heterophyllus -- slow (jackfruit)
            _ => 45,
        };
        private static int SpeciesMatureSteps(int speciesId) => speciesId switch
        {
            0 => 350,
            1 => 400,
            2 => 370,
            3 => 380,
            4 => 450,
            _ => 380,
        };
    }
}
