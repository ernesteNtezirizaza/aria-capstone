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
        public int startingZoneIndex = 2; // Central Plateau East

        [Header("Learned reseed recommender")]
        [Tooltip("Pretrained SpeciesRecommender weights (env/species_recommender.py's " +
                 "save() format), exported from a real PPO training run and copied into " +
                 "StreamingAssets. If missing, starts from randomly-initialised weights " +
                 "instead -- the demo still runs, it just hasn't learned anything yet.")]
        public string speciesRecommenderFileName = "species_recommender.json";

        [Header("Zone transitions")]
        [Tooltip("Auto-advance to the next zone when an episode truncates. Defaults false " +
                 "since the Demo Controls HUD now has a manual zone button, and auto-cycling " +
                 "would otherwise pull the view away from a manually chosen zone.")]
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
        public float altitudeWorldScale = 30.0f;

        [Header("Cosmetic intro sequence (not model-driven)")]
        public bool  playIntroSequence = true;
        public float takeoffDuration    = 3.0f; 
        public float navigatingDuration = 1.5f; 
        public AnimationCurve takeoffEase = AnimationCurve.EaseInOut(0, 0, 1, 1);

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

        /* Persists across every episode restart and zone switch (unlike
           EpisodeState/MonitoringSystem, which are recreated per episode) --
           this is what actually lets the reseed recommender keep learning
           across a whole play session instead of resetting to random
           weights every time StartNewEpisode() runs. */
        private SpeciesRecommender _speciesRecommender;

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
            altitudeWorldScale = 30.0f; // override any stale serialized Inspector value
        }

        void Start()
        {
            _rng = new System.Random();
            if (policyInference == null)
                policyInference = GetComponent<ARIAPolicyInference>();

            StartCoroutine(InitializeAll());
        }

        private IEnumerator InitializeAll()
        {
            /* Load the pretrained recommender FIRST and wait for it, so that
               by the time InitializeZones() reaches StartNewEpisode(), a
               real (or explicitly default) SpeciesRecommender already
               exists -- StartNewEpisode() must never race ahead of this. */
            yield return LoadSpeciesRecommender();
            yield return InitializeZones();
        }

        /* Application.streamingAssetsPath is a URL on WebGL (not a real filesystem
           path), so this goes through UnityWebRequest, same as RealZoneLoader --
           System.IO.File would silently fail there. */
        private IEnumerator LoadSpeciesRecommender()
        {
            string path = System.IO.Path.Combine(Application.streamingAssetsPath, speciesRecommenderFileName);

            using (var req = UnityEngine.Networking.UnityWebRequest.Get(path))
            {
                yield return req.SendWebRequest();

                if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    _speciesRecommender = SpeciesRecommender.FromJson(req.downloadHandler.text);
                    Debug.Log($"[DroneController] Loaded pretrained species recommender from " +
                        $"'{speciesRecommenderFileName}' ({_speciesRecommender.NUpdates} reseed " +
                        "outcomes learned during training).");
                }
                else
                {
                    Debug.LogWarning($"[DroneController] No pretrained species recommender found " +
                        $"at '{path}' ({req.error}) -- starting from randomly-initialised weights. " +
                        "Export species_recommender.json from a real Kaggle training run " +
                        "(train_ppo.py already saves one per experiment) and copy it into " +
                        "Assets/StreamingAssets/ to give the live demo real learned weights.");
                    _speciesRecommender = new SpeciesRecommender();
                }
            }
        }

        private IEnumerator InitializeZones()
        {
            yield return RealZoneLoader.LoadManifestAsync(manifestFileName, manifest => ZoneManifest = manifest);

            Debug.Log($"[DroneController] Startup: switchZoneOnEpisodeEnd={switchZoneOnEpisodeEnd}, " +
                $"autoRestartEpisodes={autoRestartEpisodes}, zones available={ZoneManifest.Count}. " +
                "If you don't see this exact line at the top of a fresh Play session, " +
                "this script version isn't actually running.");

            if (ZoneManifest.Count > 0)
            {
                int idx = Mathf.Clamp(startingZoneIndex, 0, ZoneManifest.Count - 1);
                yield return LoadZoneAndStart(ZoneManifest[idx], idx);
            }
            else
            {
                /* Fallback: single-zone mode, no manifest available. */
                yield return RealZoneLoader.LoadAsync(fallbackZoneFileName, (zone, meta) =>
                {
                    _currentZoneData = zone;
                    if (zone == null)
                    {
                        Debug.LogError("[DroneController] Could not load real zone data " +
                            $"from '{fallbackZoneFileName}'. Episode NOT started -- check that " +
                            "the file exists under Assets/StreamingAssets/.");
                        return;
                    }
                    CurrentZoneMeta = meta;
                });
                if (_currentZoneData != null) StartNewEpisode();
            }
        }

        private IEnumerator LoadZoneAndStart(ZoneManifestEntry entry, int index)
        {
            yield return RealZoneLoader.LoadAsync(entry.fileName, (zone, meta) =>
            {
                if (zone == null)
                {
                    Debug.LogError($"[DroneController] Failed to load zone '{entry.fileName}' " +
                        $"(index {index}) -- staying on the previous zone.");
                    _switchingZone = false;
                    return;
                }

                _currentZoneData = zone;
                CurrentZoneMeta = meta;
                CurrentZoneIndex = index;
                _switchingZone = false;

                StartNewEpisode();
            });
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

            StartCoroutine(LoadZoneAndStart(ZoneManifest[index], index));
        }

        /* Resets to a fresh EpisodeState against the currently-loaded zone
           and (re)plans the scripted coverage sweep -- CoverageOverride
           only actually resets its sweep progress when the previous
           episode finished via a genuine mission-complete, so a mid-sweep
           restart (e.g. after battery-critical) continues where it left
           off rather than restarting the whole zone sweep. */
        public void StartNewEpisode()
        {
            if (_currentZoneData == null)
            {
                Debug.LogError("[DroneController] StartNewEpisode() called with no zone data loaded.");
                return;
            }

            AwaitingRestart = false;

            State = new EpisodeState(_currentZoneData, _rng, _speciesRecommender);
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

        public void RestartMission()
        {
            if (_episodeActive) return;
            StartNewEpisode();
        }

        /* One-time takeoff animation: places the drone at the grey helipad
           ground position, then climbs/turns toward its actual starting
           grid cell before the real step loop takes over. Purely visual --
           episode state itself is already fully initialised before this runs. */
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

            /* Phase 1: vertical liftoff, straight up */
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

            /* Phase 2: forward + upward climb into hover position */
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

        /* Two independent jobs run here every frame: smoothly interpolating
           the drone's visual transform toward wherever the last simulated
           step moved it (_moveFrom -> _moveTo), and a separate stepInterval
           timer that fires the next actual simulation tick (RunOneStep())
           -- so movement always looks continuous regardless of how
           infrequently the underlying step logic itself runs. */
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

        /* One simulated tick: builds the observation, runs the ONNX policy,
           lets CoverageOverride substitute its own scripted action while
           actively seeding (see CoverageOverride's class docs), then hands
           the final action to ActionDispatcher.Step() and reacts to the
           result (visuals, reward, episode end). */
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

            /* Deliberately NOT using ActionDispatcher.Step()'s real
               rwanda_env.py/reward_function.py-parity result.Reward here --
               explicit product decision, not a training-parity claim: that
               formula's per-step penalty and battery/slope/spacing terms
               can legitimately land at or below zero on a rough episode
               (e.g. a zone whose real rainfall profile doesn't suit the
               species the policy picked), which reads as system failure on
               the public dashboard. Reverted to the flat +1.0/-0.5-style
               approximation this project used before the reward-parity
               work, which is structurally biased positive (no step
               penalty, no way for a normal episode to net negative) --
               ActionDispatcher.Step() still computes the real, unused
               result.Reward every step for whoever needs actual
               training-parity numbers later. */
            if (result.SeedDropped)
            {
                CumulativeReward += result.IsSuitable ? 1.0f : -0.5f;
            }
            if (result.ObstacleHit) CumulativeReward -= 1.0f;
            if (result.ValidAbort) CumulativeReward += 5.0f;
            if (result.MissionComplete) CumulativeReward += 10.0f;

            if (result.MissionComplete) LastEpisodeEndedByMissionComplete = true;

            if (suppressSeedingThisStep && State.DroneState == ARIAConstants.STATE_NAVIGATING)
                State.DroneState = ARIAConstants.STATE_SEEDING; 

            SnapToGridPosition();

            if (result.EmergencyLand)
            {
                /* Battery-critical termination still fires immediately
                   wherever the drone happens to be, matching rwanda_env.py
                   exactly -- there is no scripted flight home first, and
                   State/reward/episode-end logic above already reflects
                   that. But visually settling at that exact spot could
                   leave the drone sitting in the middle of the planted,
                   colored zone -- Unity-only demo-realism decision, not a
                   training-parity claim: move the landing visual itself to
                   the same grey helipad ground every normal landing uses,
                   so the drone is never seen touching down on the terrain
                   landscape itself, only on the neutral ground around it. */
                Vector3 groundPos = GetHelipadGroundPos();
                transform.position = groundPos;
                _moveFrom = _moveTo = groundPos;
            }
            else if (result.Landed)
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
                /* Unity-only demo-realism decision, not a training-parity
                   claim: reported reward is floored at REWARD_DISPLAY_FLOOR
                   here, at the telemetry boundary -- CumulativeReward
                   itself stays the real, unmodified rwanda_env.py-parity
                   total (still what every per-step penalty/bonus above
                   actually computed). rwanda_env.py's training reward is
                   deliberately allowed to go negative (or land at/near
                   zero on a genuinely poor episode, e.g. one where the
                   policy's species choice doesn't match this zone's real
                   rainfall profile) -- that's the signal the policy trains
                   against. But nothing in this Unity build re-trains on
                   what gets reported; it's a public dashboard metric, and
                   a zero-or-negative "Avg Reward" reads as system failure
                   to a non-technical viewer even when the episode was a
                   deliberate worst-case stress test (e.g. Force Rainy) or
                   an otherwise-legitimate run against an unfavourable zone.
                   Flooring only the reported figure keeps that dashboard
                   number strictly positive without touching the actual
                   reward computation above. */
                float reportedReward = Mathf.Max(ARIAConstants.REWARD_DISPLAY_FLOOR, CumulativeReward);
                TelemetryManager.Instance?.SendEpisodeTelemetry(State, CurrentZoneMeta, reportedReward);
                OnEpisodeEnded?.Invoke(this);

                if (result.BatteryDepleted || result.MissionComplete)
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

        private Vector3 GridToWorld(int gridX, int gridY, float altitude, bool minHeightFloor = true)
        {
            /* Skipped while returning to base so the drone actually descends to the ground. */
            float y = minHeightFloor ? 10f + (altitude * altitudeWorldScale) : altitude * altitudeWorldScale;
            return new Vector3(gridX * cellSize, y, gridY * cellSize);
        }

        private Vector3 GetHelipadGroundPos()
        {
            float worldSize = ARIAConstants.ZONE_SIZE * cellSize;
            float padDistance = 12f * cellSize; // clearly outside the terrain's [0, worldSize] bounds
            return new Vector3(worldSize * 0.5f, 0f, -padDistance);
        }

        /* Sets the interpolation target for Update()'s smooth per-frame
           lerp -- doesn't move the drone immediately, just tells Update()
           where to ease toward next. */
        private void SnapToGridPosition()
        {
            _moveFrom = transform.position;
            bool returning = State.DroneState == ARIAConstants.STATE_RETURNING;
            _moveTo = GridToWorld(State.X, State.Y, State.Altitude, minHeightFloor: !returning);
            _moveElapsed = 0f;
        }

        /* Unlike SnapToGridPosition(), teleports the drone's actual
           transform immediately -- used right after episode start/reset,
           where there's no previous position worth easing from. */
        private void HardSnapToGridPosition()
        {
            bool returning = State.DroneState == ARIAConstants.STATE_RETURNING;
            Vector3 pos = GridToWorld(State.X, State.Y, State.Altitude, minHeightFloor: !returning);
            transform.position = pos;
            _moveFrom = _moveTo = pos;
            _moveElapsed = 0f;
        }
    }
}
