using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using ARIA.Core;

namespace ARIA.Systems
{
    [System.Serializable]
    public class TelemetryZone
    {
        public string name;
        public string agro_zone;
        public float area_km2;
        public string split_type;
    }

    [System.Serializable]
    public class TelemetryEpisode
    {
        public string agent_type;
        public float total_reward;
        public float pct_suitable_seeded;
        public float mean_soil_score;
        public float species_entropy;
        public int spacing_violations;
        public int protected_area_seeds;
        public int n_seeds_placed;
    }

    [System.Serializable]
    public class TelemetrySeed
    {
        public float x_coord;
        public float y_coord;
        public int species_id;
        public float soil_score;
        public float rain_score;
        public float slope_score;
        public bool is_suitable;
        public bool in_protected_area;
        // Seed-monitoring: lifecycle stage + failure info, so the dashboard can
        // show what happened to each seed and why the drone rescheduled it.
        public string stage;
        public string fail_reason;
        public int dropped_at_step;   // simulation timestep
        public int failed_at_step;    // simulation timestep
        public string dropped_at;     // real wall-clock time, ISO 8601 UTC
        public string failed_at;      // real wall-clock time, ISO 8601 UTC
    }

    [System.Serializable]
    public class TelemetryPayload
    {
        public TelemetryZone zone;
        public TelemetryEpisode episode;
        public List<TelemetrySeed> seeds;
    }

    public class TelemetryManager : MonoBehaviour
    {
        public static TelemetryManager Instance { get; private set; }
        
        [Tooltip("The URL of the ARIA_Web Next.js dashboard API")]
        public string apiEndpoint = "";

        private void Awake()
        {
#if UNITY_EDITOR
            LoadEnvFile();
#endif
            if (Instance == null)
            {
                Instance = this;
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// Gathers statistics from the EpisodeState and sends them via HTTP POST to the Web Dashboard.
        /// </summary>
        public void SendEpisodeTelemetry(EpisodeState state, float finalReward, RealZoneJson zoneMeta = null)
        {
            StartCoroutine(PostTelemetryCoroutine(state, finalReward, zoneMeta));
        }

        private IEnumerator PostTelemetryCoroutine(EpisodeState state, float finalReward, RealZoneJson zoneMeta)
        {
            // Build the payload -- zone name/agro-zone/split come from the real
            // loaded zone file when available; area is computed from the zone's
            // real bounds. There's no real province data anywhere in the zone
            // JSON, and it wasn't displayed on the dashboard anyway, so it's
            // removed rather than sent as a hardcoded/placeholder value.
            TelemetryPayload payload = new TelemetryPayload
            {
                zone = new TelemetryZone
                {
                    name = zoneMeta != null ? zoneMeta.name : "Simulated Zone Alpha",
                    agro_zone = zoneMeta != null ? zoneMeta.agroZone : "Highlands",
                    area_km2 = CalculateZoneAreaKm2(zoneMeta),
                    split_type = zoneMeta != null ? zoneMeta.split : "Grid"
                },
                episode = new TelemetryEpisode
                {
                    agent_type = "PPO_CNN_Agent",
                    total_reward = finalReward,
                    pct_suitable_seeded = CalculateSuitableSeededPct(state),
                    mean_soil_score = CalculateMeanSoilScore(state),
                    species_entropy = CalculateSpeciesEntropy(state),
                    spacing_violations = CalculateSpacingViolations(state),
                    protected_area_seeds = CalculateProtectedAreaSeeds(state),
                    n_seeds_placed = (int)ARIAConstants.INITIAL_SEEDS - (int)state.SeedsRemaining
                },
                seeds = BuildSeedList(state)
            };

            string jsonData = JsonUtility.ToJson(payload);
            Debug.Log($"[TelemetryManager] Sending Payload: {jsonData}");

            using (UnityWebRequest request = new UnityWebRequest(apiEndpoint, "POST"))
            {
                byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(jsonData);
                request.uploadHandler = new UploadHandlerRaw(bodyRaw);
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");

                yield return request.SendWebRequest();

                if (request.result == UnityWebRequest.Result.ConnectionError || request.result == UnityWebRequest.Result.ProtocolError)
                {
                    Debug.LogError($"[TelemetryManager] Failed to send telemetry: {request.error}\nResponse: {request.downloadHandler.text}");
                }
                else
                {
                    Debug.Log($"[TelemetryManager] Successfully sent telemetry to dashboard! Response: {request.downloadHandler.text}");
                }
            }
        }

        // Real computation, mirroring rwanda_env.py's _metrics():
        //   n_suit   = plantable cells minus cells too close to a protected area
        //   n_seeded = seeds the drone actually dropped where IsSuitable was true
        //   pct      = n_seeded / max(n_suit, 1)
        // This previously returned state.ZoneSuitability() + a random jitter --
        // a number with no connection to which seeds were actually placed well,
        // dressed up to look like a real metric. Fixed to compute the real thing.
        // Real computation from the zone's actual lat/lon bounds (already
        // loaded in RealZoneJson -- boundsLeft/Right/Top/Bottom -- but never
        // used for this). This previously sent 150.5f for every single zone
        // regardless of its real size.
        //
        // Approximates the bounding box as flat (fine at Rwanda's ~2-degree
        // extent): 1 degree latitude ~= 111.32km; 1 degree longitude ~=
        // 111.32km * cos(latitude), since longitude lines converge toward
        // the poles.
        private float CalculateZoneAreaKm2(RealZoneJson zoneMeta)
        {
            if (zoneMeta == null) return 150.5f; // no real zone loaded -- honest fallback, not a fabricated per-zone number

            double latSpanDeg = zoneMeta.boundsTop - zoneMeta.boundsBottom;
            double lonSpanDeg = zoneMeta.boundsRight - zoneMeta.boundsLeft;
            double avgLatRad  = (zoneMeta.boundsTop + zoneMeta.boundsBottom) / 2.0 * (System.Math.PI / 180.0);

            double kmPerDegLat = 111.32;
            double kmPerDegLon = 111.32 * System.Math.Cos(avgLatRad);

            double areaKm2 = System.Math.Abs(latSpanDeg) * kmPerDegLat
                            * System.Math.Abs(lonSpanDeg) * kmPerDegLon;
            return (float)areaKm2;
        }

        private float CalculateSuitableSeededPct(EpisodeState state)
        {
            int nPlantable = 0, nNearProtected = 0;
            for (int y = 0; y < state.Zone.Size; y++)
            {
                for (int x = 0; x < state.Zone.Size; x++)
                {
                    if (!state.Zone.NoPlant[y, x]) nPlantable++;
                    if (state.Zone.DistGrid[y, x] >= ARIAConstants.PROTECTED_PROXIMITY_THRESHOLD) nNearProtected++;
                }
            }
            int nSuit = Mathf.Max(nPlantable - nNearProtected, 1);

            int nSeeded = 0;
            foreach (var seed in state.Growth.Seeds.Values)
                if (seed.IsSuitable) nSeeded++;

            return Mathf.Clamp01((float)nSeeded / nSuit);
        }

        // Real computation, mirroring rwanda_env.py's _metrics():
        //   H = -sum(p_i * ln(p_i)) over each species' share of total drops,
        //   0 if only zero or one distinct species was ever used this episode.
        // This previously returned a hardcoded 0.5f regardless of whether the
        // drone planted one species everywhere or five in equal proportion --
        // a number with no connection to actual planting behaviour.
        private float CalculateSpeciesEntropy(EpisodeState state)
        {
            var counts = new Dictionary<int, int>();
            foreach (var seed in state.Growth.Seeds.Values)
            {
                if (!counts.ContainsKey(seed.SpeciesId)) counts[seed.SpeciesId] = 0;
                counts[seed.SpeciesId]++;
            }

            var nonZero = new List<float>();
            foreach (var c in counts.Values)
                if (c > 0) nonZero.Add(c);

            if (nonZero.Count <= 1) return 0f;

            float total = 0f;
            foreach (var c in nonZero) total += c;

            float h = 0f;
            foreach (var c in nonZero)
            {
                float p = c / total;
                h -= p * Mathf.Log(p); // natural log, matches Python's np.log
            }
            return h;
        }

        // Real computation, mirroring rwanda_env.py's _metrics():
        //   mean_soil_score = mean(soil_score at each seed's actual drop location)
        // This previously averaged state.Zone.SoilAt(y,x) over the ENTIRE zone
        // grid -- a static number describing the zone's geography, unrelated to
        // where the drone actually flew or how well it placed seeds. A mission
        // that planted only in poor soil and one that planted only in rich soil
        // would have reported the identical value. Fixed to measure actual
        // placement quality: each Seed already carries its own SoilScore from
        // the moment it was dropped (see GrowthEngine.Register), so this just
        // averages that, the same as Python does over its seeds list.
        private float CalculateMeanSoilScore(EpisodeState state)
        {
            float sum = 0f;
            int count = 0;
            foreach (var seed in state.Growth.Seeds.Values)
            {
                sum += seed.SoilScore;
                count++;
            }
            return count > 0 ? sum / count : 0f;
        }

        // Real computation, mirroring rwanda_env.py's _metrics():
        //   protected_area_seeds = count of seeds dropped with InProtected == true
        // This was a hardcoded 0 regardless of whether the drone actually
        // planted inside a protected-area buffer -- Seed.InProtected was already
        // tracked per-seed (see GrowthEngine.Register) and simply never counted.
        // Real computation, mirroring rwanda_env.py's _metrics():
        //   spacing_violations = count of seed PAIRS planted closer together
        //   than MIN_SEED_SPACING (Manhattan distance)
        // This previously returned state.Disturbance.Events.Count -- the
        // number of ANIMAL DISTURBANCE incidents, a completely different,
        // unrelated quantity mislabeled under the wrong metric name. The
        // dashboard's "spacing violations" column was showing disturbance
        // events, not seed clustering, for every live episode.
        private int CalculateSpacingViolations(EpisodeState state)
        {
            var positions = new List<(int x, int y)>();
            foreach (var seed in state.Growth.Seeds.Values)
                positions.Add((seed.X, seed.Y));

            int violations = 0;
            for (int i = 0; i < positions.Count; i++)
            {
                for (int j = i + 1; j < positions.Count; j++)
                {
                    int manhattan = Mathf.Abs(positions[i].x - positions[j].x)
                                   + Mathf.Abs(positions[i].y - positions[j].y);
                    if (manhattan < ARIAConstants.MIN_SEED_SPACING) violations++;
                }
            }
            return violations;
        }

        private int CalculateProtectedAreaSeeds(EpisodeState state)
        {
            int count = 0;
            foreach (var seed in state.Growth.Seeds.Values)
                if (seed.InProtected) count++;
            return count;
        }

        // Reports every seed the drone actually dropped this episode, with its
        // real lifecycle stage and (for dead seeds) why/when it failed --
        // sourced from the monitoring system's persistent failure log rather
        // than fabricated placements.
        private List<TelemetrySeed> BuildSeedList(EpisodeState state)
        {
            var seedList = new List<TelemetrySeed>();
            foreach (var seed in state.Growth.Seeds.Values)
            {
                string failReason = null;
                int failedAtStep = -1;
                string failedAtIso = null;
                if (seed.Stage == SeedStage.Dead)
                {
                    // Most recent matching log entry -- FailedCellsLog persists
                    // across the whole run, so scan from the end for freshness.
                    for (int i = state.Monitor.FailedCellsLog.Count - 1; i >= 0; i--)
                    {
                        var f = state.Monitor.FailedCellsLog[i];
                        if (f.X == seed.X && f.Y == seed.Y && f.SpeciesTried == seed.SpeciesId)
                        {
                            failReason  = f.Reason;
                            failedAtStep = f.FailedAt;
                            failedAtIso  = f.FailedAtUtc.ToString("o");   // ISO 8601 round-trip
                            break;
                        }
                    }
                }

                seedList.Add(new TelemetrySeed {
                    x_coord = seed.X,
                    y_coord = seed.Y,
                    species_id = seed.SpeciesId,
                    soil_score = seed.SoilScore,
                    rain_score = seed.RainScore,
                    slope_score = seed.SlopeScore,
                    is_suitable = seed.IsSuitable,
                    in_protected_area = seed.InProtected,
                    stage = seed.Stage.ToString(),
                    fail_reason = failReason,
                    dropped_at_step = seed.DroppedAt,
                    failed_at_step = failedAtStep,
                    dropped_at = seed.DroppedAtUtc.ToString("o"),
                    failed_at = failedAtIso,
                });
            }
            return seedList;
        }
        private void LoadEnvFile()
        {
            string envPath = System.IO.Path.Combine(System.IO.Directory.GetParent(Application.dataPath).FullName, ".env");
            if (System.IO.File.Exists(envPath))
            {
                string[] lines = System.IO.File.ReadAllLines(envPath);
                foreach (string line in lines)
                {
                    if (line.TrimStart().StartsWith("API_ENDPOINT="))
                    {
                        apiEndpoint = line.Substring(line.IndexOf('=') + 1).Trim();
                    }
                }
            }
        }
    }
}
