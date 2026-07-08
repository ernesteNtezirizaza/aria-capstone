using UnityEngine;
using UnityEngine.UI;
using ARIA.Drone;
using ARIA.Core;

namespace ARIA.UI
{
    public class DroneHUD : MonoBehaviour
    {
        [Header("Source")]
        public DroneController drone;

        [Tooltip("Assigned by SceneBootstrapper -- lets the Obstacles button refresh " +
                 "floating markers immediately on click, not just at episode boundaries.")]
        public AerialObstacleVisualizer obstacleVisualizer;

        private Text _batteryText;
        private Text _coverText;
        private Text _seedsText;
        private Text _queuedText;

        private Text _weatherButtonLabel, _obstacleButtonLabel, _animalButtonLabel, _zoneButtonLabel;
        private Image _weatherButtonImg, _obstacleButtonImg, _animalButtonImg;

        private GameObject _restartBar;
        private Button _restartButton;

        void Awake()
        {
            BuildLayout();
        }

        void OnEnable()
        {
            if (drone != null) Subscribe();
        }

        void OnDisable()
        {
            if (drone != null) Unsubscribe();
        }

        public void Bind(DroneController d)
        {
            if (drone != null) Unsubscribe();
            drone = d;
            if (drone != null) Subscribe();
        }

        private void Subscribe()
        {
            drone.OnStepTaken += HandleStep;
            drone.OnEpisodeStarted += HandleEpisodeStarted;
            drone.OnAwaitingRestart += HandleAwaitingRestart;
        }

        private void Unsubscribe()
        {
            drone.OnStepTaken -= HandleStep;
            drone.OnEpisodeStarted -= HandleEpisodeStarted;
            drone.OnAwaitingRestart -= HandleAwaitingRestart;
        }

        // ── Layout construction ──────────────────────────────────────

        private void BuildLayout()
        {
            // Small standalone battery display -- top-left, deliberately
            // compact (not a large metrics block) so it can't cover the terrain.
            var batteryPanel = MakePanel(transform, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(10, -10), new Vector2(220, 34), new Color(0f, 0.05f, 0f, 1f));
            _batteryText = MakeText(batteryPanel, "Battery: --", 15, TextAnchor.MiddleCenter,
                new Vector2(8, 0), new Vector2(-8, 0));
            _batteryText.fontStyle = FontStyle.Bold;
            _batteryText.color = new Color(0.3f, 1f, 0.3f);

            var coverPanel = MakePanel(transform, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(10, -48), new Vector2(220, 30), new Color(0f, 0.05f, 0f, 1f));
            _coverText = MakeText(coverPanel, "Cover: --", 13, TextAnchor.MiddleCenter,
                new Vector2(8, 0), new Vector2(-8, 0));
            _coverText.fontStyle = FontStyle.Bold;
            _coverText.color = new Color(0.7f, 0.85f, 1f);

            var seedsPanel = MakePanel(transform, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(10, -82), new Vector2(220, 30), new Color(0f, 0.05f, 0f, 1f));
            _seedsText = MakeText(seedsPanel, "Seeds: --", 13, TextAnchor.MiddleCenter,
                new Vector2(8, 0), new Vector2(-8, 0));
            _seedsText.fontStyle = FontStyle.Bold;
            _seedsText.color = new Color(0.85f, 0.95f, 0.5f);

            var queuedPanel = MakePanel(transform, new Vector2(0, 1), new Vector2(0, 1),
                new Vector2(10, -116), new Vector2(220, 30), new Color(0f, 0.05f, 0f, 1f));
            _queuedText = MakeText(queuedPanel, "Seeds Queued: --", 13, TextAnchor.MiddleCenter,
                new Vector2(8, 0), new Vector2(-8, 0));
            _queuedText.fontStyle = FontStyle.Bold;
            _queuedText.color = new Color(1f, 0.7f, 0.3f);

            BuildDemoControls();
            BuildRestartBar();
        }

        private void BuildDemoControls()
        {
            var panel = MakePanel(transform, new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-16, -16), new Vector2(260, 236), new Color(0.1f, 0.15f, 0.1f, 0.98f));

            var titleText = MakeText(panel, "DEMO CONTROLS", 16, TextAnchor.UpperCenter,
                new Vector2(0, -12), new Vector2(0, -12));
            titleText.color = new Color(1f, 1f, 1f);
            titleText.fontStyle = FontStyle.Bold;

            // Weather button -- cycles Sunny(Default) -> ForceSunny -> ForceRainy.
            var weatherBtnGO = MakePanel(panel.transform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(12, -40), new Vector2(-24, 40), new Color(0.25f, 0.25f, 0.15f, 1f));
            var weatherBtn = weatherBtnGO.AddComponent<Button>();
            _weatherButtonImg = weatherBtnGO.GetComponent<Image>();
            _weatherButtonLabel = MakeText(weatherBtnGO, "Weather: Sunny (Default)", 14, TextAnchor.MiddleCenter,
                new Vector2(6, 0), new Vector2(-6, 0));
            weatherBtn.onClick.AddListener(CycleWeatherMode);

            // Obstacle toggle -- ClearObstacles() resets to real data when turned off.
            var obstacleBtnGO = MakePanel(panel.transform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(12, -88), new Vector2(-24, 40), new Color(0.25f, 0.15f, 0.15f, 1f));
            var obstacleBtn = obstacleBtnGO.AddComponent<Button>();
            _obstacleButtonImg = obstacleBtnGO.GetComponent<Image>();
            _obstacleButtonLabel = MakeText(obstacleBtnGO, "Obstacles: Off", 14, TextAnchor.MiddleCenter,
                new Vector2(6, 0), new Vector2(-6, 0));
            obstacleBtn.onClick.AddListener(ToggleObstacles);

            // Animal disturbance toggle -- insects that can kill nearby seeds.
            var animalBtnGO = MakePanel(panel.transform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(12, -136), new Vector2(-24, 40), new Color(0.2f, 0.15f, 0.1f, 1f));
            var animalBtn = animalBtnGO.AddComponent<Button>();
            _animalButtonImg = animalBtnGO.GetComponent<Image>();
            _animalButtonLabel = MakeText(animalBtnGO, "Animal Disturbance: Off", 14, TextAnchor.MiddleCenter,
                new Vector2(6, 0), new Vector2(-6, 0));
            animalBtn.onClick.AddListener(ToggleAnimalDisturbance);

            // Zone button -- cycles through every real zone in the manifest.
            var zoneBtnGO = MakePanel(panel.transform, new Vector2(0, 1), new Vector2(1, 1),
                new Vector2(12, -184), new Vector2(-24, 40), new Color(0.1f, 0.16f, 0.22f, 1f));
            var zoneBtn = zoneBtnGO.AddComponent<Button>();
            _zoneButtonLabel = MakeText(zoneBtnGO, "Zone: --", 14, TextAnchor.MiddleCenter,
                new Vector2(6, 0), new Vector2(-6, 0));
            zoneBtn.onClick.AddListener(CycleZone);

            RefreshDemoControlLabels();
        }

        private void BuildRestartBar()
        {
            var bar = MakePanel(transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0, 40), new Vector2(420, 70), new Color(0.05f, 0.05f, 0.05f, 0.95f));
            var rt = bar.GetComponent<RectTransform>();
            rt.pivot = new Vector2(0.5f, 0f);

            var label = MakeText(bar, "Mission Complete", 13, TextAnchor.UpperCenter,
                new Vector2(10, -26), new Vector2(-10, -6));
            label.color = new Color(1f, 0.7f, 0.3f);
            label.fontStyle = FontStyle.Bold;

            var btnGO = MakePanel(bar.transform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(-90, 8), new Vector2(180, 26), new Color(0.15f, 0.45f, 0.15f, 1f));
            _restartButton = btnGO.AddComponent<Button>();
            var btnLabel = MakeText(btnGO, "Restart Mission", 13, TextAnchor.MiddleCenter,
                new Vector2(6, 0), new Vector2(-6, 0));
            btnLabel.fontStyle = FontStyle.Bold;
            _restartButton.onClick.AddListener(() =>
            {
                if (drone != null) drone.RestartMission();
                _restartBar.SetActive(false);
            });

            bar.SetActive(false);
            _restartBar = bar;
        }

        private void CycleWeatherMode()
        {
            DemoConditions.WeatherMode = DemoConditions.WeatherMode switch
            {
                WeatherMode.RealData   => WeatherMode.ForceSunny,
                WeatherMode.ForceSunny => WeatherMode.ForceRainy,
                _                      => WeatherMode.RealData,
            };
            RefreshDemoControlLabels();
        }

        private void ToggleObstacles()
        {
            DemoConditions.ObstacleOverlayEnabled = !DemoConditions.ObstacleOverlayEnabled;
            if (!DemoConditions.ObstacleOverlayEnabled && drone != null && drone.State != null)
            {
                DemoConditions.ClearObstacles(drone.State.Zone);
            }
            if (obstacleVisualizer != null) obstacleVisualizer.RefreshMarkers();
            RefreshDemoControlLabels();
        }

        private void ToggleAnimalDisturbance()
        {
            DemoConditions.AnimalDisturbanceEnabled = !DemoConditions.AnimalDisturbanceEnabled;
            RefreshDemoControlLabels();
        }

        private void CycleZone()
        {
            if (drone == null || drone.ZoneManifest == null || drone.ZoneManifest.Count <= 1) return;
            drone.SwitchZone((drone.CurrentZoneIndex + 1) % drone.ZoneManifest.Count);
            if (_zoneButtonLabel != null) _zoneButtonLabel.text = "Zone: Switching...";
        }

        private void RefreshDemoControlLabels()
        {
            string modeText = DemoConditions.WeatherMode switch
            {
                WeatherMode.RealData   => "Weather: Sunny (Default)",
                WeatherMode.ForceSunny => "Weather: Force Sunny",
                WeatherMode.ForceRainy => "Weather: Force Rainy",
                _ => "Weather: ?",
            };
            _weatherButtonLabel.text = modeText;
            _weatherButtonImg.color = DemoConditions.WeatherMode == WeatherMode.RealData
                ? new Color(0.2f, 0.2f, 0.15f, 0.95f)
                : new Color(0.5f, 0.4f, 0.05f, 0.95f);

            _obstacleButtonLabel.text = DemoConditions.ObstacleOverlayEnabled
                ? "Obstacles: On" : "Obstacles: Off";
            _obstacleButtonImg.color = DemoConditions.ObstacleOverlayEnabled
                ? new Color(0.6f, 0.15f, 0.1f, 0.95f)
                : new Color(0.2f, 0.15f, 0.15f, 0.95f);

            _animalButtonLabel.text = DemoConditions.AnimalDisturbanceEnabled
                ? "Animal Disturbance: On" : "Animal Disturbance: Off";
            _animalButtonImg.color = DemoConditions.AnimalDisturbanceEnabled
                ? new Color(0.55f, 0.35f, 0.05f, 0.95f)
                : new Color(0.2f, 0.15f, 0.1f, 0.95f);

            if (_zoneButtonLabel != null)
            {
                if (drone != null && drone.ZoneManifest != null && drone.ZoneManifest.Count > 0 && drone.CurrentZoneIndex >= 0)
                {
                    string zoneName = drone.CurrentZoneMeta != null ? drone.CurrentZoneMeta.name : "Zone";
                    _zoneButtonLabel.text = $"Zone: {zoneName} ({drone.CurrentZoneIndex + 1}/{drone.ZoneManifest.Count})";
                }
                else
                {
                    _zoneButtonLabel.text = "Zone: --";
                }
            }
        }

        private GameObject MakePanel(Transform parent, Vector2 anchorMin, Vector2 anchorMax,
                                      Vector2 pos, Vector2 size, Color col)
        {
            var go = new GameObject("Panel");
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = col;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin; rt.anchorMax = anchorMax;
            rt.pivot = anchorMin;
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            return go;
        }

        private Text MakeText(GameObject parent, string content, int size,
                               TextAnchor anchor, Vector2 offsetMin, Vector2 offsetMax)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent.transform, false);
            var t = go.AddComponent<Text>();
            t.text = content; t.fontSize = size; t.alignment = anchor;
            t.color = Color.white;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one;
            rt.offsetMin = offsetMin; rt.offsetMax = offsetMax;
            return t;
        }

        // ── Real data wiring ─────────────────────────────────────────

        private void HandleEpisodeStarted(DroneController d)
        {
            if (_restartBar != null) _restartBar.SetActive(false);

            if (DemoConditions.ObstacleOverlayEnabled && d.State != null)
            {
                DemoConditions.ApplyObstacleOverlay(d.State.Zone, d.CurrentZoneIndex);
            }

            RefreshDemoControlLabels(); // picks up the new zone name/index once a switch completes
        }

        private void HandleAwaitingRestart(DroneController d)
        {
            if (_restartBar != null) _restartBar.SetActive(true);
        }

        private void HandleStep(DroneController d)
        {
            if (d.State != null)
            {
                float pct = d.State.Energy.GetState() * 100f;
                _batteryText.text = $"Battery: {pct:F0}%";
                _batteryText.color = pct < 15f ? new Color(1f, 0.25f, 0.2f)   // critical -- red
                                   : pct < 35f ? new Color(1f, 0.8f, 0.2f)    // low -- amber
                                   : new Color(0.3f, 1f, 0.3f);               // normal -- green

                _coverText.text = d.State.CoverDeployed ? "Cover: Deployed" : "Cover: Retracted";
                _coverText.color = d.State.CoverDeployed ? new Color(0.5f, 0.75f, 1f) : new Color(0.6f, 0.6f, 0.6f);

                int remaining = Mathf.Max(0, Mathf.RoundToInt(d.State.SeedsRemaining));
                int total = Mathf.RoundToInt(ARIAConstants.INITIAL_SEEDS);
                _seedsText.text = $"Seeds: {remaining}/{total}";
                _seedsText.color = remaining <= 0 ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.85f, 0.95f, 0.5f);

                int queued = d.State.ReseedingTargets.Count;
                _queuedText.text = $"Seeds Queued: {queued}";
                _queuedText.color = queued > 0 ? new Color(1f, 0.7f, 0.3f) : new Color(0.6f, 0.6f, 0.6f);
            }
        }
    }
}
