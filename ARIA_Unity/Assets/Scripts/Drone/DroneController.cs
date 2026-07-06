using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ARIA.Core;
using ARIA.Systems;
using ARIA.ML;

namespace ARIA.Drone
{
    public class DroneController : MonoBehaviour
    {
        [Header("ML")]
        public ARIAPolicyInference policyInference;

        [Header("Real zone data")]
        [Tooltip("Manifest listing all real zones available to switch between " +
                 "(produced alongside the batch zone export). If missing, falls " +
                 "back to loading fallbackZoneFileName with no switching UI.")]
        public string manifestFileName = "zone_manifest.json";

        [Tooltip("Used only if zone_manifest.json is missing/empty.")]
        public string fallbackZoneFileName = "aria_zone.json";

        [Tooltip("Which manifest entry to start on (index into zone_manifest.json's list).")]
        public int startingZoneIndex = 2; // Central Plateau East, if using the 9-zone export from this conversation

        [Header("Zone transitions")]
        [Tooltip("If true, moves to the next zone in the manifest ONLY when the " +
                 "current mission genuinely concludes (seeds depleted, battery-" +
                 "critical emergency landing, or max-steps truncation) -- a real " +
                 "simulation event, not a timer. IMPORTANT: the trained model's " +
                 "47-action space is entirely within-episode (direction, species, " +
                 "hover, abort, cover, altitude, emergency) -- it has no 'visit " +
                 "zone N' action. Zone assignment happens at the environment/reset " +
                 "level in the real Python training setup too, not as a model " +
                 "decision. This flag controls an ORCHESTRATION layer around the " +
                 "model, not something the model itself decides.")]
        public bool switchZoneOnEpisodeEnd = false;

        [Header("Simulation speed")]
        [Tooltip("Seconds between each policy step. Lower = faster demo, " +
                 "higher = easier to follow visually.")]
        public float stepInterval = 0.15f;

        [Tooltip("If true, runs a new episode automatically on the SAME real " +
                 "zone when the current one terminates (drone position/seeds/ " +
                 "etc. reset, but the terrain itself is the same real place).")]
        public bool autoRestartEpisodes = true;

        [Header("Visual scale")]
        [Tooltip("World-space size of one terrain cell, for converting " +
                 "grid (x,y) into a Unity world position.")]
        public float cellSize = 1.0f;
        public float altitudeWorldScale = 60.0f;

        [Header("Cosmetic intro sequence (NOT model-driven, see file header)")]
        public bool  playIntroSequence = true;
        public float takeoffDuration    = 3.0f; 
        public float navigatingDuration = 1.5f; 
        public AnimationCurve takeoffEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

        // ── Runtime state ─────────────────────────────────────────
        public EpisodeState State { get; private set; }
        public int    LastAction      { get; private set; }
        public string LastActionDesc { get; private set; }
        public StepResult LastResult { get; private set; }
        public int    EpisodeCount    { get; private set; }
        public float  CumulativeReward { get; private set; }

        public RealZoneJson CurrentZoneMeta { get; private set; }

        public List<ZoneManifestEntry> ZoneManifest { get; private set; } = new List<ZoneManifestEntry>();

        public int CurrentZoneIndex { get; private set; } = -1;

        public bool  IsPlayingIntro    { get; private set; }
        public int   IntroDisplayState { get; private set; } // STATE_TAKEOFF or STATE_NAVIGATING during intro

        public bool AwaitingRestart { get; private set; }

        public bool LastEpisodeEndedByMissionComplete { get; private set; }

        public System.Action<DroneController> OnAwaitingRestart;

        private System.Random _rng;
        private float _timer;
        private bool  _episodeActive;
        private bool  _stepLoopEnabled; // true only once intro (if any) has finished
        private bool  _switchingZone;

        private Vector3 _moveFrom, _moveTo;
        private float _moveElapsed;

        private ZoneData _currentZoneData;

        public System.Action<DroneController> OnEpisodeStarted;
        public System.Action<DroneController> OnEpisodeEnded;
        public System.Action<DroneController> OnStepTaken;
        public System.Action<DroneController> OnIntroStarted;
        public System.Action<DroneController> OnIntroFinished;

        public System.Action<DroneController> OnBeforeStep;

        void Awake()
        {
            altitudeWorldScale = 60.0f; // Force this value to override any serialized Unity Inspector value
        }

        void Start()
        {
            _rng = new System.Random();
            if (policyInference == null)
                policyInference = GetComponent<ARIAPolicyInference>();

            ZoneManifest = RealZoneLoader.LoadManifest(manifestFileName);

            Debug.Log($"[DroneController] Startup: switchZoneOnEpisodeEnd={switchZoneOnEpisodeEnd}, " +
                $"autoRestartEpisodes={autoRestartEpisodes}, zones available={ZoneManifest.Count}. " +
                "If you don't see this exact line at the top of a fresh Play session, " +
                "this script version isn't actually running.");

            if (ZoneManifest.Count > 0)
            {
                int idx = Mathf.Clamp(startingZoneIndex, 0, ZoneManifest.Count - 1);
                SwitchZone(idx);
            }
            else
            {
                // Fallback: single-zone mode, no manifest available.
                _currentZoneData = RealZoneLoader.Load(out var meta, fallbackZoneFileName);
                DemoConditions.ApplyObstacleOverlay(_currentZoneData, 0);
                if (_currentZoneData == null)
                {
                    Debug.LogError("[DroneController] Could not load real zone data " +
                        $"from '{fallbackZoneFileName}'. Episode NOT started -- check that " +
                        "the file exists under Assets/StreamingAssets/.");
                    return;
                }
                CurrentZoneMeta = meta;
                StartNewEpisode();
            }
        }

        public void SwitchZone(int index)
        {
            if (ZoneManifest == null || ZoneManifest.Count == 0)
            {
                Debug.LogWarning("[DroneController] SwitchZone() called but no zone manifest is loaded.");
                return;
            }
            index = Mathf.Clamp(index, 0, ZoneManifest.Count - 1);

            _switchingZone = true;
            _episodeActive = false;
            StopAllCoroutines();

            var entry = ZoneManifest[index];
            var zone = RealZoneLoader.Load(out var meta, entry.fileName);
            if (zone == null)
            {
                Debug.LogError($"[DroneController] Failed to load zone '{entry.fileName}' " +
                    $"(index {index}) -- staying on the previous zone.");
                _switchingZone = false;
                return;
            }

            _currentZoneData = zone;
            DemoConditions.ApplyObstacleOverlay(_currentZoneData, index);
            CurrentZoneMeta = meta;
            CurrentZoneIndex = index;
            _switchingZone = false;

            StartNewEpisode();
        }

        public void StartNewEpisode()
        {
            if (_currentZoneData == null)
            {
                Debug.LogError("[DroneController] StartNewEpisode() called with no zone data loaded.");
                return;
            }

            AwaitingRestart = false;

            State = new EpisodeState(_currentZoneData, _rng);
            EpisodeCount++;
            CumulativeReward = 0f;

            if (LastEpisodeEndedByMissionComplete)
                CoverageOverride.Reset();

            CoverageOverride.PlanForZone(State.Zone, (int)ARIAConstants.INITIAL_SEEDS);

            _episodeActive = true;
            _timer = 0f;

            OnEpisodeStarted?.Invoke(this);
            LastEpisodeEndedByMissionComplete = false;

            if (playIntroSequence)
            {
                _stepLoopEnabled = false;
                StartCoroutine(IntroSequence());
            }
            else
            {
                _stepLoopEnabled = true;
                HardSnapToGridPosition();
            }
        }

        public void RestartAfterBatteryDepletion()
        {
            if (_episodeActive) return;
            StartNewEpisode();
        }

        private IEnumerator IntroSequence()
        {
            IsPlayingIntro = true;
            OnIntroStarted?.Invoke(this);

            IntroDisplayState = ARIAConstants.STATE_TAKEOFF;
            Vector3 hoverPos = GridToWorld(State.X, State.Y, altitude: State.Altitude);

            Vector3 groundPos = GetHelipadGroundPos();
            Vector3 climbDirection = (hoverPos - groundPos);
            climbDirection.y = 0f;
            if (climbDirection.sqrMagnitude < 0.01f) climbDirection = Vector3.forward;
            climbDirection.Normalize();

            transform.position = groundPos;
            transform.rotation = Quaternion.LookRotation(climbDirection, Vector3.up);
            _moveFrom = _moveTo = transform.position;

            // ── PHASE 1: vertical liftoff -- straight up, no forward ──
            float liftoffDuration = Mathf.Max(0.6f, takeoffDuration * 0.3f);
            float liftoffHeight = 4f * cellSize;
            Vector3 liftoffPos = groundPos + Vector3.up * liftoffHeight;
            float t = 0f;
            while (t < liftoffDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / liftoffDuration);
                transform.position = Vector3.Lerp(groundPos, liftoffPos, k);
                yield return null;
            }
            transform.position = liftoffPos;

            // ── PHASE 2: forward + upward climb into hover position ──
            float climbDuration = Mathf.Max(0.4f, takeoffDuration - liftoffDuration);
            t = 0f;
            while (t < climbDuration)
            {
                t += Time.deltaTime;
                float k = takeoffEase.Evaluate(Mathf.Clamp01(t / climbDuration));
                transform.position = Vector3.Lerp(liftoffPos, hoverPos, k);

                float pitch = Mathf.Sin(k * Mathf.PI) * 18f; // degrees, airplane-style nose-up
                transform.rotation = Quaternion.LookRotation(climbDirection, Vector3.up)
                    * Quaternion.Euler(-pitch, 0f, 0f);

                yield return null;
            }
            transform.position = hoverPos;
            transform.rotation = Quaternion.identity;
            _moveFrom = _moveTo = hoverPos;

            IntroDisplayState = ARIAConstants.STATE_NAVIGATING;

            float navT = 0f;
            while (navT < navigatingDuration)
            {
                navT += Time.deltaTime;
                yield return null;
            }
            _moveFrom = _moveTo = hoverPos;

            IsPlayingIntro = false;
            _stepLoopEnabled = true;
            OnIntroFinished?.Invoke(this);
        }

        void Update()
        {
            if (_episodeActive && _stepLoopEnabled && !IsPlayingIntro)
            {
                _moveElapsed += Time.deltaTime;
                float k = stepInterval > 0f ? Mathf.Clamp01(_moveElapsed / stepInterval) : 1f;
                transform.position = Vector3.Lerp(_moveFrom, _moveTo, k);

                Vector3 delta = _moveTo - _moveFrom;
                delta.y = 0f;
                if (delta.sqrMagnitude > 0.0001f)
                {
                    Quaternion desired = Quaternion.LookRotation(delta.normalized, Vector3.up);
                    transform.rotation = Quaternion.Slerp(transform.rotation, desired, Time.deltaTime * 6f);
                }
            }

            if (!_episodeActive || !_stepLoopEnabled) return;

            _timer += Time.deltaTime;
            if (_timer < stepInterval) return;
            _timer = 0f;

            RunOneStep();
        }

        private void RunOneStep()
        {
            if (policyInference == null)
            {
                Debug.LogWarning("[DroneController] No ARIAPolicyInference assigned -- cannot step.");
                return;
            }

            OnBeforeStep?.Invoke(this);

            var obs = State.BuildObservation();
            float[] logits = policyInference.Infer(obs);
            int action = ActionSelector.SelectArgmax(logits);
            bool overridden = false;
            bool suppressSeedingThisStep = false;

            if (State.DroneState == ARIAConstants.STATE_SEEDING &&
                CoverageOverride.TryGetOverrideAction(State, out int coverageAction, out bool suppressSeeding))
            {
                action = coverageAction;
                overridden = true;
                suppressSeedingThisStep = suppressSeeding;
            }

            LastAction = action;
            LastActionDesc = (overridden ? "[Coverage sweep] " : "") + ActionSelector.Describe(action);

            if (suppressSeedingThisStep) State.DroneState = ARIAConstants.STATE_NAVIGATING;

            var result = ActionDispatcher.Step(State, action, _rng);
            LastResult = result;
            State.LastResult = result; // keep EpisodeState in sync for TerrainRenderer etc.

            // ── Calculate a real-time reward approximation for the dashboard ──
            if (result.SeedDropped)
            {
                CumulativeReward += result.IsSuitable ? 1.0f : -0.5f;
                SpawnSeedVisual();
            }
            if (result.ObstacleHit) CumulativeReward -= 1.0f;
            if (result.ValidAbort) CumulativeReward += 5.0f;
            if (result.MissionComplete) CumulativeReward += 10.0f;

            if (result.MissionComplete) LastEpisodeEndedByMissionComplete = true;

            if (suppressSeedingThisStep && State.DroneState == ARIAConstants.STATE_NAVIGATING)
                State.DroneState = ARIAConstants.STATE_SEEDING; 

            SnapToGridPosition();

            if (result.Landed || result.EmergencyLand)
            {
                Vector3 padPos = GetHelipadGroundPos();
                if (result.Terminated)
                {
                    transform.position = padPos;
                    _moveFrom = _moveTo = padPos;
                }
                else
                {
                    _moveTo = padPos;
                }
            }

            OnStepTaken?.Invoke(this);

            if (result.Terminated || result.Truncated)
            {
                _episodeActive = false;
                TelemetryManager.Instance?.SendEpisodeTelemetry(State, CumulativeReward);
                OnEpisodeEnded?.Invoke(this);

                if (result.BatteryDepleted)
                {
                    AwaitingRestart = true;
                    OnAwaitingRestart?.Invoke(this);
                    return;
                }

                if (switchZoneOnEpisodeEnd && ZoneManifest.Count > 1)
                {
                    SwitchZone((CurrentZoneIndex + 1) % ZoneManifest.Count);
                }
                else if (autoRestartEpisodes)
                {
                    StartNewEpisode();
                }
            }
        }

        private Vector3 GridToWorld(int gridX, int gridY, float altitude)
        {
            // Enforce a minimum base height (e.g., 20) so it always hovers far above the ground
            return new Vector3(gridX * cellSize, 20f + (altitude * altitudeWorldScale), gridY * cellSize);
        }

        private void SpawnSeedVisual()
        {
            GameObject seed = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            seed.transform.position = transform.position - new Vector3(0, 1.5f, 0); // Drop slightly below drone
            seed.transform.localScale = new Vector3(2.5f, 2.5f, 2.5f); // Visible green balls
            var renderer = seed.GetComponent<Renderer>();
            renderer.material.color = new Color(0.1f, 0.9f, 0.2f); // Bright green
            
            var rb = seed.AddComponent<Rigidbody>();
            rb.mass = 1f;
            rb.linearDamping = 0.5f;
            
            // Clean up the seed objects after they hit the ground so they don't clutter the scene
            Destroy(seed, 4f);
        }

        private Vector3 GetHelipadGroundPos()
        {
            float worldSize = ARIAConstants.ZONE_SIZE * cellSize;
            float padDistance = 12f * cellSize; // clearly outside the terrain's [0, worldSize] bounds
            return new Vector3(worldSize * 0.5f, 0f, -padDistance);
        }

        private void SnapToGridPosition()
        {
            _moveFrom = transform.position;
            _moveTo = GridToWorld(State.X, State.Y, State.Altitude);
            _moveElapsed = 0f;
        }

        private void HardSnapToGridPosition()
        {
            Vector3 pos = GridToWorld(State.X, State.Y, State.Altitude);
            transform.position = pos;
            _moveFrom = _moveTo = pos;
            _moveElapsed = 0f;
        }
    }
}
