using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARIA.Systems
{
    /// <summary>
    /// env/species_recommender.py, ported to C# for the live Unity build.
    ///
    /// Online learned scorer: score(cell, species) = sigmoid(w . features + b)
    ///
    /// This replaces MonitoringSystem's previous hardcoded rules:
    ///   species: "if disturbance/dry, species 0, else species_tried+1"
    ///   priority: "(soil + rain) / 2", ignoring slope and corridor risk
    /// with the same learned, feedback-driven model the Python training side
    /// uses (see CHANGES_AND_VALIDATION.md sections 5 and 9).
    ///
    /// Weight file compatibility: <see cref="ToJson"/>/<see cref="FromJson"/>
    /// use the exact same JSON shape Python's SpeciesRecommender.save()/
    /// load() produce -- {"w": [...], "b": ..., "n_features": 7,
    /// "n_updates": ...} -- so a recommender trained during a real PPO run
    /// on Kaggle can be exported and dropped into StreamingAssets so the live
    /// demo starts from real learned weights instead of small random ones.
    /// </summary>
    [Serializable]
    public class SpeciesRecommenderWeights
    {
        public float[] w;
        public float b;
        public int n_features;
        public int n_updates;
    }

    public class SpeciesRecommender
    {
        public const int N_FEATURES = 7;

        public float[] W;
        public float B;
        public int NUpdates;
        public float Lr;

        private readonly System.Random _rng;

        public SpeciesRecommender(float lr = 0.08f, int seed = 42)
        {
            Lr = lr;
            _rng = new System.Random(seed);
            W = new float[N_FEATURES];
            for (int i = 0; i < N_FEATURES; i++)
                W[i] = (float)(NextGaussian() * 0.05);
            B = 0f;
            NUpdates = 0;
        }

        // Box-Muller, since System.Random has no built-in Gaussian sampler.
        private double NextGaussian()
        {
            double u1 = 1.0 - _rng.NextDouble();
            double u2 = 1.0 - _rng.NextDouble();
            return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2);
        }

        private static float Clamp(float v, float lo, float hi) => v < lo ? lo : (v > hi ? hi : v);

        /// <summary>
        /// Same 7 features as the Python version's _make_features:
        /// [soil, rain, slope_pen, corridor_proximity, disturbance_flag,
        ///  rain_gap (site_rain - species.rain_min), species_mature_steps_norm]
        /// </summary>
        public static float[] MakeFeatures(float soil, float rain, float slopePen,
            float corridorProximity, bool isDisturbance,
            float speciesRainMin, float speciesMatureStepsNorm)
        {
            float rainGap = Clamp(rain - speciesRainMin, -1f, 1f);
            return new float[] {
                soil, rain, slopePen, corridorProximity,
                isDisturbance ? 1f : 0f, rainGap, speciesMatureStepsNorm,
            };
        }

        public float Score(float[] features)
        {
            float z = B;
            for (int i = 0; i < N_FEATURES; i++) z += W[i] * features[i];
            return 1f / (1f + (float)Math.Exp(-z));
        }

        /// <summary>
        /// Scores every candidate species (0..nSpecies-1, excluding
        /// excludeSpecies if it's a valid index) and picks the best one
        /// epsilon-greedily -- mirrors Python's recommend(..., return_features=True,
        /// return_score=True). The returned score doubles as MonitoringSystem's
        /// reseed priority for this cell.
        /// </summary>
        public int Recommend(
            float soil, float rain, float slopePen, float corridorProximity, bool isDisturbance,
            float[] speciesRainMin, Func<int, float> speciesMatureStepsNorm, int nSpecies,
            int excludeSpecies, float epsilon,
            out float[] chosenFeatures, out float chosenScore)
        {
            var candidates = new List<int>();
            for (int s = 0; s < nSpecies; s++)
                if (s != excludeSpecies) candidates.Add(s);
            if (candidates.Count == 0)
                for (int s = 0; s < nSpecies; s++) candidates.Add(s);

            var feats  = new Dictionary<int, float[]>();
            var scores = new Dictionary<int, float>();
            foreach (var s in candidates)
            {
                var f = MakeFeatures(soil, rain, slopePen, corridorProximity, isDisturbance,
                                      speciesRainMin[s], speciesMatureStepsNorm(s));
                feats[s]  = f;
                scores[s] = Score(f);
            }

            int chosen;
            if (_rng.NextDouble() < epsilon)
            {
                chosen = candidates[_rng.Next(candidates.Count)];
            }
            else
            {
                chosen = candidates[0];
                float best = scores[chosen];
                foreach (var s in candidates)
                {
                    if (scores[s] > best) { best = scores[s]; chosen = s; }
                }
            }

            chosenFeatures = feats[chosen];
            chosenScore    = scores[chosen];
            return chosen;
        }

        /// <summary>
        /// outcome: 1.0 if the replanted seed matured, 0.0 if it died again.
        /// One logistic-regression SGD step, identical update rule to the
        /// Python version.
        /// </summary>
        public void Update(float[] features, float outcome)
        {
            float pred  = Score(features);
            float error = outcome - pred;
            for (int i = 0; i < N_FEATURES; i++)
                W[i] += Lr * error * features[i];
            B += Lr * error;
            NUpdates++;
        }

        public string ToJson()
        {
            var data = new SpeciesRecommenderWeights
            {
                w = W, b = B, n_features = N_FEATURES, n_updates = NUpdates,
            };
            return JsonUtility.ToJson(data, prettyPrint: true);
        }

        /// <summary>
        /// Loads weights from JSON in the same shape Python's
        /// SpeciesRecommender.save() writes, e.g. from StreamingAssets after
        /// exporting a recommender trained during real PPO training.
        /// </summary>
        public static SpeciesRecommender FromJson(string json, float lr = 0.08f)
        {
            var data = JsonUtility.FromJson<SpeciesRecommenderWeights>(json);
            var rec = new SpeciesRecommender(lr);
            if (data != null && data.w != null && data.w.Length == N_FEATURES)
            {
                rec.W = data.w;
                rec.B = data.b;
                rec.NUpdates = data.n_updates;
            }
            else
            {
                Debug.LogWarning("[SpeciesRecommender] JSON weights missing/malformed -- "
                    + "keeping randomly-initialised weights instead.");
            }
            return rec;
        }
    }
}
