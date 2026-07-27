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
    }

    [System.Serializable]
    public class TelemetryEpisode
    {
        public float pct_suitable_seeded;
        public int spacing_violations;
        public int protected_area_seeds;
        public int reseeding_count;
        // Real cumulative episode reward, mirroring rwanda_env.py's
        // episode_reward exactly (see ActionDispatcher.Step()'s per-step
        // reward computation) -- not a training-side-only metric anymore.
        public float reward;
    }

    [System.Serializable]
    public class TelemetrySeed
    {
        // Seed-monitoring: lifecycle stage + failure info, so the dashboard can
        // show what happened to each seed and why the drone rescheduled it.
        public string stage;
        public string fail_reason;
        public int dropped_at_step;   // simulation timestep
    }

    [System.Serializable]
    public class TelemetryPayload
    {
        public TelemetryZone zone;
        public TelemetryEpisode episode;
        public List<TelemetrySeed> seeds;
        // 0 means "no logged-in user for this session" -- JsonUtility has no
        // clean nullable-int support, and real DB user ids start at 1
        // (autoincrement), so 0 is a safe "none" sentinel.
        public int user_id;
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
        public void SendEpisodeTelemetry(EpisodeState state, RealZoneJson zoneMeta = null, float episodeReward = 0f)
        {
            StartCoroutine(PostTelemetryCoroutine(state, zoneMeta, episodeReward));
        }

        private IEnumerator PostTelemetryCoroutine(EpisodeState state, RealZoneJson zoneMeta, float episodeReward)
        {
            // Zone name/agro-zone come from the real loaded zone file when available.
            TelemetryPayload payload = new TelemetryPayload
            {
                zone = new TelemetryZone
                {
                    name = zoneMeta != null ? zoneMeta.name : "Simulated Zone Alpha",
                    agro_zone = zoneMeta != null ? zoneMeta.agroZone : "Highlands",
                },
                episode = new TelemetryEpisode
                {
                    pct_suitable_seeded = CalculateSuitableSeededPct(state),
                    spacing_violations = CalculateSpacingViolations(state),
                    protected_area_seeds = CalculateProtectedAreaSeeds(state),
                    reseeding_count = CalculateReseedingCount(state),
                    reward = episodeReward
                },
                seeds = BuildSeedList(state),
                user_id = GetUserIdFromUrl()
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

        // Real computation, mirroring rwanda_env.py's _metrics():
        //   protected_area_seeds = count of seeds dropped with InProtected == true
        // This was a hardcoded 0 regardless of whether the drone actually
        // planted inside a protected-area buffer -- Seed.InProtected was already
        // tracked per-seed (see GrowthEngine.Register) and simply never counted.
        private int CalculateProtectedAreaSeeds(EpisodeState state)
        {
            int count = 0;
            foreach (var seed in state.Growth.Seeds.Values)
                if (seed.InProtected) count++;
            return count;
        }

        // Real computation, mirroring growth_engine.py's summary():
        //   reseeding_count = number of (x, y) cells that carry both a Dead
        //   seed AND a currently-alive seed -- i.e. cells where a failed
        //   seed was successfully replanted, not just the size of the
        //   pending reseed queue.
        private int CalculateReseedingCount(EpisodeState state)
        {
            var deadPos = new HashSet<(int, int)>();
            var alivePos = new HashSet<(int, int)>();
            foreach (var seed in state.Growth.Seeds.Values)
            {
                if (seed.Stage == SeedStage.Dead) deadPos.Add((seed.X, seed.Y));
                else alivePos.Add((seed.X, seed.Y));
            }
            deadPos.IntersectWith(alivePos);
            return deadPos.Count;
        }

        // Reports every seed the drone actually dropped this episode, with its
        // real lifecycle stage and (for dead seeds) why it failed -- sourced
        // from the monitoring system's persistent failure log rather than
        // fabricated placements.
        private List<TelemetrySeed> BuildSeedList(EpisodeState state)
        {
            var seedList = new List<TelemetrySeed>();
            foreach (var seed in state.Growth.Seeds.Values)
            {
                string failReason = null;
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
                            break;
                        }
                    }
                }

                seedList.Add(new TelemetrySeed {
                    stage = seed.Stage.ToString(),
                    fail_reason = failReason,
                    dropped_at_step = seed.DroppedAt,
                });
            }
            return seedList;
        }
        // The web app's /simulation page appends ?uid=<id> to the iframe's own
        // src when a user is logged in (see ARIA_Web's simulation/page.tsx),
        // so the WebGL build's own document URL carries it -- read straight
        // off Application.absoluteURL rather than needing a JS bridge call.
        private int GetUserIdFromUrl()
        {
            string url = Application.absoluteURL;
            int qIndex = string.IsNullOrEmpty(url) ? -1 : url.IndexOf('?');
            if (qIndex < 0) return 0;

            string query = url.Substring(qIndex + 1);
            foreach (string pair in query.Split('&'))
            {
                string[] kv = pair.Split('=');
                if (kv.Length == 2 && kv[0] == "uid" && int.TryParse(kv[1], out int uid))
                {
                    return uid;
                }
            }
            return 0;
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
