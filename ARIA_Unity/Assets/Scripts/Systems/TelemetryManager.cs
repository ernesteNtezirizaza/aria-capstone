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
        public string province;
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
        public int dropped_at;
        public int failed_at;
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
            // loaded zone file when available; province/area have no real-data
            // source yet, so they stay as placeholder constants.
            TelemetryPayload payload = new TelemetryPayload
            {
                zone = new TelemetryZone
                {
                    name = zoneMeta != null ? zoneMeta.name : "Simulated Zone Alpha",
                    province = "Kigali",
                    agro_zone = zoneMeta != null ? zoneMeta.agroZone : "Highlands",
                    area_km2 = 150.5f,
                    split_type = zoneMeta != null ? zoneMeta.split : "Grid"
                },
                episode = new TelemetryEpisode
                {
                    agent_type = "PPO_CNN_Agent",
                    total_reward = finalReward,
                    pct_suitable_seeded = CalculateSuitableSeededPct(state),
                    mean_soil_score = CalculateMeanSoilScore(state),
                    species_entropy = 0.5f,
                    spacing_violations = state.Disturbance.Events.Count, 
                    protected_area_seeds = 0,
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

        private float CalculateSuitableSeededPct(EpisodeState state)
        {
            // Calculate a synthetic suitability percentage based on the zone suitability
            return Mathf.Clamp01(state.ZoneSuitability() + Random.Range(-0.1f, 0.2f));
        }

        private float CalculateMeanSoilScore(EpisodeState state)
        {
            float sum = 0f;
            int count = 0;
            for (int y = 0; y < state.Zone.Size; y++)
            {
                for (int x = 0; x < state.Zone.Size; x++)
                {
                    sum += state.Zone.SoilAt(y, x);
                    count++;
                }
            }
            return count > 0 ? sum / count : 0f;
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
                int failedAt = -1;
                if (seed.Stage == SeedStage.Dead)
                {
                    // Most recent matching log entry -- FailedCellsLog persists
                    // across the whole run, so scan from the end for freshness.
                    for (int i = state.Monitor.FailedCellsLog.Count - 1; i >= 0; i--)
                    {
                        var f = state.Monitor.FailedCellsLog[i];
                        if (f.X == seed.X && f.Y == seed.Y && f.SpeciesTried == seed.SpeciesId)
                        {
                            failReason = f.Reason;
                            failedAt = f.FailedAt;
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
                    dropped_at = seed.DroppedAt,
                    failed_at = failedAt,
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
