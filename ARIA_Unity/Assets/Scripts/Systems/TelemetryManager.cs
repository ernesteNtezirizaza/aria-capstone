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
        public void SendEpisodeTelemetry(EpisodeState state, float finalReward)
        {
            StartCoroutine(PostTelemetryCoroutine(state, finalReward));
        }

        private IEnumerator PostTelemetryCoroutine(EpisodeState state, float finalReward)
        {
            // Build the payload
            TelemetryPayload payload = new TelemetryPayload
            {
                zone = new TelemetryZone
                {
                    name = "Simulated Zone Alpha",
                    province = "Kigali",
                    agro_zone = "Highlands",
                    area_km2 = 150.5f,
                    split_type = "Grid"
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

        private List<TelemetrySeed> BuildSeedList(EpisodeState state)
        {
            var seedList = new List<TelemetrySeed>();
            int numSeeds = (int)ARIAConstants.INITIAL_SEEDS - (int)state.SeedsRemaining;
            
            // Generate some representative seed placements across the plantable terrain
            for(int i = 0; i < numSeeds; i++)
            {
                int rx = Random.Range(5, state.Zone.Size - 5);
                int ry = Random.Range(5, state.Zone.Size - 5);

                seedList.Add(new TelemetrySeed {
                    x_coord = rx,
                    y_coord = ry,
                    species_id = Random.Range(0, ARIAConstants.N_SPECIES),
                    soil_score = state.Zone.SoilAt(ry, rx),
                    rain_score = state.Zone.Terrain[ry, rx, 3],
                    slope_score = state.Zone.Terrain[ry, rx, 1],
                    is_suitable = !state.Zone.NoPlant[ry, rx],
                    in_protected_area = state.Zone.DistGrid[ry, rx] > 0.8f
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
