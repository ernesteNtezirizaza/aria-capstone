using System.Collections.Generic;
using UnityEngine;
using ARIA.Core;
using ARIA.Systems;

namespace ARIA.Core
{

    public class Observation
    {
        public float[] TerrainWindow;   // [OBS_WINDOW * OBS_WINDOW * N_CHANNELS], NHWC flattened
        public float[] DroneVector;     // [10]
        public float[] CoverageMap;     // [ZONE_SIZE * ZONE_SIZE * 1]
        public float[] LifecycleMap;    // [ZONE_SIZE * ZONE_SIZE * 1]
        public float[] DisturbanceMap;  // [ZONE_SIZE * ZONE_SIZE * 1]
        public float[] ObstacleMap;     // [ZONE_SIZE * ZONE_SIZE * 1]
        public float[] MissionVector;   // [14] -- reseed-target + nearest-unseeded-suitable-cell direction/distance
        public float[] TerrainStats;    // [6]
    }

    public class EpisodeState
    {
        public int   X, Y;
        public float Altitude;
        public float SeedsRemaining;
        public int   Timestep;
        public int   Season;
        public bool  CoverDeployed;
        public int   DroneState;
        public int   BaseX, BaseY;
        public bool  AbortTriggered;
        // Mirrors rwanda_env.py's self.valid_abort_rewarded: a valid abort
        // only pays REWARD_BATTERY_SAVE the first time per episode, so a
        // policy can't camp on repeated free-abort attempts for reward.
        public bool  ValidAbortRewarded;
        public int   MissionsCompleted;
        public int   ObstaclesAvoided;

        public bool  MissionCompleteReturning;

        public int   PlantableCells;
        public int   CoveredPlantableCells;

        public StepResult LastResult;

        public float[,] CoverageMap;          // [ZONE_SIZE, ZONE_SIZE]
        public Dictionary<int, int> SpeciesCounts;
        public HashSet<(int y, int x)> ReseedingTargets;
        public Dictionary<(int y, int x), int> ReseedSpeciesMap; // recommended species per reseed target

        // Precomputed once per episode (terrain doesn't change mid-episode): a cell
        // that's genuinely worth seeding, matching env/rwanda_env.py's suitable_mask.
        // Python's version uses the max rainfall across all 6 seasons ("best month");
        // Unity's zone export only carries a single rainfall snapshot per cell, not a
        // seasonal stack, so this uses that one value instead. Same shape of signal,
        // slightly less precise -- a real gap, not a rounding difference.
        private bool[,] _suitableMask;

        public ZoneData        Zone;
        public GrowthEngine    Growth;
        public DisturbanceEngine Disturbance;
        public MonitoringSystem Monitor;
        public WeatherSystem   Weather;
        public EnergySystem    Energy;

        public EpisodeState(ZoneData zone, System.Random rng, SpeciesRecommender speciesRecommender = null)
        {
            Zone        = zone;
            Growth      = new GrowthEngine(ARIAConstants.ZONE_SIZE, rng);
            Disturbance = new DisturbanceEngine(rng);
            Monitor     = new MonitoringSystem(speciesRecommender);
            Weather     = new WeatherSystem();
            Energy      = new EnergySystem();
            ResetEpisode(rng);
        }

        public void ResetEpisode(System.Random rng = null)
        {
            rng = rng ?? new System.Random();
            (X, Y) = ValidStart(rng);

            Altitude = 1.0f;
            SeedsRemaining = ARIAConstants.INITIAL_SEEDS;
            Timestep = 0;
            Season = 0;
            CoverDeployed = false;
            DroneState = ARIAConstants.STATE_SEEDING;
            CoverageMap = new float[ARIAConstants.ZONE_SIZE, ARIAConstants.ZONE_SIZE];
            SpeciesCounts = new Dictionary<int, int>();
            for (int i = 0; i < ARIAConstants.N_SPECIES; i++) SpeciesCounts[i] = 0;
            MissionsCompleted = 0;
            ObstaclesAvoided = 0;
            AbortTriggered = false;
            ValidAbortRewarded = false;
            MissionCompleteReturning = false;
            BaseX = ARIAConstants.ZONE_SIZE / 2;
            BaseY = 0; // Bottom edge of the grid, nearest to the helipad
            ReseedingTargets = new HashSet<(int, int)>();
            ReseedSpeciesMap = new Dictionary<(int, int), int>();
            LastResult = default;

            CoveredPlantableCells = 0;
            PlantableCells = 0;
            float minRainAcrossSpecies = ARIAConstants.SPECIES_RAIN_MIN[0];
            for (int i = 1; i < ARIAConstants.SPECIES_RAIN_MIN.Length; i++)
                minRainAcrossSpecies = Mathf.Min(minRainAcrossSpecies, ARIAConstants.SPECIES_RAIN_MIN[i]);
            _suitableMask = new bool[ARIAConstants.ZONE_SIZE, ARIAConstants.ZONE_SIZE];
            for (int y = 0; y < ARIAConstants.ZONE_SIZE; y++)
            {
                for (int x = 0; x < ARIAConstants.ZONE_SIZE; x++)
                {
                    if (!Zone.NoPlant[y, x]) PlantableCells++;
                    _suitableMask[y, x] = !Zone.NoPlant[y, x]
                        && Zone.DistGrid[y, x] < 0.9f
                        && Zone.Terrain[y, x, 2] >= ARIAConstants.ZONE_MIN_SOIL
                        && Zone.Terrain[y, x, 3] >= minRainAcrossSpecies;
                }
            }

            Growth.Reset();
            Disturbance.Reset();
            Monitor.Reset(); // intentionally a no-op -- monitoring persists
            Weather.Reset();
            Energy.Reset();
        }

        private (int x, int y) ValidStart(System.Random rng)
        {
            int size = ARIAConstants.ZONE_SIZE;
            for (int i = 0; i < 100; i++)
            {
                int x = rng.Next(5, size - 5);
                int y = rng.Next(5, size - 5);
                if (!Zone.NoPlant[y, x] && Zone.DistGrid[y, x] < 0.9f)
                    return (x, y);
            }
            return (size / 2, size / 2);
        }

        public float ZoneSuitability() => Zone.ZoneSuitability();

        public float[] TerrainStats()
        {
            float elevSum = 0f, slopeSum = 0f, soilSum = 0f, rainSum = 0f, lcSum = 0f;
            int plantableCount = 0;
            int size = ARIAConstants.ZONE_SIZE;

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    elevSum  += Zone.Terrain[y, x, 0];
                    slopeSum += Zone.Terrain[y, x, 1];
                    soilSum  += Zone.Terrain[y, x, 2];
                    rainSum  += Zone.Terrain[y, x, 3];
                    lcSum    += Zone.Terrain[y, x, 4];
                    if (!Zone.NoPlant[y, x]) plantableCount++;
                }
            }

            int n = size * size;
            float elev  = (elevSum / n) / 3000f;   
            float slope = slopeSum / n;
            float soil  = soilSum / n;
            float rain  = rainSum / n;
            float lc    = (lcSum / n) / 10f;       
            float plant = (float)plantableCount / n;

            return new float[]
            {
                Mathf.Clamp01(elev), Mathf.Clamp01(slope), Mathf.Clamp01(soil),
                Mathf.Clamp01(rain), Mathf.Clamp01(lc),    Mathf.Clamp01(plant),
            };
        }

        public Observation BuildObservation()
        {
            var obs = new Observation();
            int size = ARIAConstants.ZONE_SIZE;
            int win  = ARIAConstants.OBS_WINDOW;
            int half = win / 2;
            int ch   = ARIAConstants.N_CHANNELS;

            obs.TerrainWindow = new float[win * win * ch];
            int idx = 0;
            for (int wy = 0; wy < win; wy++)
            {
                for (int wx = 0; wx < win; wx++)
                {
                    int sy = Mathf.Clamp(Y - half + wy, 0, size - 1); // edge padding
                    int sx = Mathf.Clamp(X - half + wx, 0, size - 1);
                    for (int c = 0; c < ch; c++)
                        obs.TerrainWindow[idx++] = Sanitise(Zone.Terrain[sy, sx, c]);
                }
            }

            float distBase = Mathf.Sqrt(
                (X - BaseX) * (X - BaseX) + (Y - BaseY) * (Y - BaseY))
                / (size * 1.4f);

            obs.DroneVector = new float[]
            {
                Mathf.Clamp01((float)X / (size - 1)),
                Mathf.Clamp01((float)Y / (size - 1)),
                Mathf.Clamp01(Altitude),
                Mathf.Clamp01(Energy.GetState()),
                Mathf.Clamp01(Energy.SolarInput / 0.0015f), // matches /0.0015 in source
                Mathf.Clamp01(SeedsRemaining / ARIAConstants.INITIAL_SEEDS),
                Mathf.Clamp01((float)Weather.WeatherState),
                Mathf.Clamp01(CoverDeployed ? 1f : 0f),
                Mathf.Clamp01(distBase),
                Mathf.Clamp01((float)DroneState / 6f),
            };

            obs.CoverageMap    = FlattenSingleChannel(CoverageMap);
            obs.LifecycleMap   = FlattenSingleChannel(Growth.LifecycleMap());
            obs.DisturbanceMap = FlattenSingleChannel(Zone.DistGrid);
            obs.ObstacleMap    = FlattenSingleChannel(Zone.ObsGrid);

            float zoneScore  = ZoneSuitability();
            float rainMean   = MeanOfChannel(3);
            float coveredPct = MeanOf2D(CoverageMap);
            float failedN    = Mathf.Min(Monitor.QueueSize() / 50f, 1f);
            float reseedN    = Mathf.Min(ReseedingTargets.Count / 10f, 1f);
            float abortScore = zoneScore < ARIAConstants.ZONE_MIN_SUITABILITY ? 1f : 0f;
            float isReseed    = ReseedingTargets.Count > 0 ? 1f : 0f;

            // Previously the policy only ever saw a COUNT of queued reseed
            // targets, never where they actually are -- it had no way to
            // learn "navigate back to a failed cell", matching the same gap
            // found and fixed on the Python training side. Must produce the
            // exact same 11-dim mission_vector shape the exported ONNX
            // policy was actually trained on, or live inference breaks.
            float relDy = 0f, relDx = 0f, manhattanDist = 1f; // no target -> neutral direction, "far" sentinel
            if (ReseedingTargets.Count > 0)
            {
                int bestDist = int.MaxValue;
                foreach (var t in ReseedingTargets)
                {
                    int d = Mathf.Abs(t.y - Y) + Mathf.Abs(t.x - X);
                    if (d < bestDist)
                    {
                        bestDist = d;
                        relDy = (float)(t.y - Y) / ARIAConstants.ZONE_SIZE;   // roughly [-1, 1]
                        relDx = (float)(t.x - X) / ARIAConstants.ZONE_SIZE;
                    }
                }
                manhattanDist = (float)bestDist / (2f * ARIAConstants.ZONE_SIZE);
            }
            float reseedDy   = Mathf.Clamp01((relDy + 1f) / 2f);   // 0.5 = same row as drone
            float reseedDx   = Mathf.Clamp01((relDx + 1f) / 2f);   // 0.5 = same column as drone
            float reseedDist = Mathf.Clamp01(manhattanDist);

            // Same idea applied to genuine coverage: direction/distance to the
            // nearest cell that's both suitable (_suitableMask) and not yet
            // seeded. Same reasoning as the reseed offset above -- without a
            // direct signal, the policy can only find unexplored suitable
            // ground by chance CNN pattern-matching, not by steering to it.
            float covRelDy = 0f, covRelDx = 0f, covManhattanDist = 1f;
            int bestCovDist = int.MaxValue;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (!_suitableMask[y, x] || CoverageMap[y, x] != 0f) continue;
                    int d = Mathf.Abs(y - Y) + Mathf.Abs(x - X);
                    if (d < bestCovDist)
                    {
                        bestCovDist = d;
                        covRelDy = (float)(y - Y) / ARIAConstants.ZONE_SIZE;
                        covRelDx = (float)(x - X) / ARIAConstants.ZONE_SIZE;
                    }
                }
            }
            if (bestCovDist != int.MaxValue)
                covManhattanDist = (float)bestCovDist / (2f * ARIAConstants.ZONE_SIZE);
            float coverageDy   = Mathf.Clamp01((covRelDy + 1f) / 2f);
            float coverageDx   = Mathf.Clamp01((covRelDx + 1f) / 2f);
            float coverageDist = Mathf.Clamp01(covManhattanDist);

            obs.MissionVector = new float[]
            {
                Mathf.Clamp01(zoneScore),
                Mathf.Clamp01(rainMean),
                Mathf.Clamp01(coveredPct),
                Mathf.Clamp01(failedN),
                Mathf.Clamp01(reseedN),
                Mathf.Clamp01(abortScore),
                Mathf.Clamp01(MissionsCompleted / 10f),
                Mathf.Clamp01(isReseed),
                reseedDy, reseedDx, reseedDist,
                coverageDy, coverageDx, coverageDist,
            };

            obs.TerrainStats = TerrainStats();

            return obs;
        }

        private float Sanitise(float v)
        {
            if (float.IsNaN(v)) return 0f;
            if (float.IsPositiveInfinity(v)) return 1f;
            if (float.IsNegativeInfinity(v)) return 0f;
            return v;
        }

        private float[] FlattenSingleChannel(float[,] map)
        {
            int size = ARIAConstants.ZONE_SIZE;
            var flat = new float[size * size];
            int idx = 0;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    flat[idx++] = Sanitise(map[y, x]);
            return flat;
        }

        private float MeanOfChannel(int channel)
        {
            float sum = 0f;
            int size = ARIAConstants.ZONE_SIZE;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    sum += Zone.Terrain[y, x, channel];
            return sum / (size * size);
        }

        private float MeanOf2D(float[,] map)
        {
            float sum = 0f;
            int size = ARIAConstants.ZONE_SIZE;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    sum += map[y, x];
            return sum / (size * size);
        }
    }
}