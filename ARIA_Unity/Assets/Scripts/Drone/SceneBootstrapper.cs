using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using Unity.InferenceEngine;
using ARIA.ML;
using ARIA.Drone;
using ARIA.UI;

namespace ARIA.Drone
{
    public class SceneBootstrapper : MonoBehaviour
    {
        [Header("Model")]
        [Tooltip("Drag aria_policy.onnx here manually (from anywhere under Assets/, " +
                 "not just Resources/). If assigned, this is used directly and " +
                 "onnxResourceName/Resources.Load below is skipped entirely.")]
        public ModelAsset onnxModel;

        [Tooltip("Only used as a fallback if onnxModel above is left empty -- " +
                 "loads Assets/Resources/<name>.onnx automatically.")]
        public string onnxResourceName = "aria_policy";

        [Header("Scene layout")]
        public float cellSize = 1.0f;
        
        public float altitudeWorldScale = 16.0f;
        public bool  buildCamera = true;
        public bool  buildLight  = true;
        public bool  buildHud    = true;

        void Awake()
        {
            GameObject droneObj = BuildDrone();
            var (obstacleVisualizer, reseedVisualizer) = BuildTerrain(droneObj);

            if (buildLight)  BuildLight();
            if (buildCamera) BuildCamera(droneObj);
            BuildRainEffect(droneObj);
            BuildCoverIndicator(droneObj);
            if (buildHud)    BuildHud(droneObj, obstacleVisualizer);
        }

        private void BuildCoverIndicator(GameObject droneObj)
        {
            var cover = droneObj.AddComponent<CoverIndicator>();
            cover.Bind(droneObj.GetComponent<DroneController>());
        }

        private void BuildRainEffect(GameObject droneObj)
        {
            GameObject rainObj = new GameObject("RainEffect");
            rainObj.AddComponent<ParticleSystem>();
            var rain = rainObj.AddComponent<RainEffect>();
            rain.followTarget = Camera.main != null ? Camera.main.transform : droneObj.transform;
            rain.Bind(droneObj.GetComponent<DroneController>());
        }

        private GameObject BuildDrone()
        {
            GameObject drone = DroneBuilder.Build();
            drone.name = "Drone";
            drone.transform.localScale = Vector3.one * 3.5f;

            var inference = drone.AddComponent<ARIAPolicyInference>();

            ModelAsset model = onnxModel;
            if (model == null)
            {
                model = Resources.Load<ModelAsset>(onnxResourceName);
            }
            if (model == null)
            {
                Debug.LogError($"[SceneBootstrapper] No model found -- neither the manual " +
                    $"'onnxModel' field on SceneBootstrapper nor Resources.Load('{onnxResourceName}') " +
                    "found anything. Either drag aria_policy.onnx into the 'Onnx Model' field on " +
                    "this GameObject's SceneBootstrapper component in the Inspector (recommended, " +
                    "most reliable), or copy it into Assets/Resources/ for the automatic fallback. " +
                    "The scene will still build, but inference will fail.");
            }
            inference.onnxModelAsset = model;

            if (model != null)
            {
                inference.Initialise();
            }

            var controller = drone.AddComponent<DroneController>();
            controller.policyInference = inference;
            controller.cellSize = cellSize;
            controller.altitudeWorldScale = altitudeWorldScale;

            return drone;
        }

        private (AerialObstacleVisualizer, ReseedMarkerVisualizer) BuildTerrain(GameObject droneObj)
        {
            GameObject terrainObj = new GameObject("RealTerrain");
            terrainObj.AddComponent<MeshRenderer>();

            var renderer = terrainObj.AddComponent<RealTerrainRenderer>();
            renderer.cellSize = cellSize;
            renderer.Bind(droneObj.GetComponent<DroneController>());

            var treeManager = terrainObj.AddComponent<SeedTreeManager>();
            treeManager.terrainRenderer = renderer;
            treeManager.cellSize = cellSize;
            treeManager.Bind(droneObj.GetComponent<DroneController>());

            var obstacleVisualizer = terrainObj.AddComponent<AerialObstacleVisualizer>();
            obstacleVisualizer.cellSize = cellSize;
            obstacleVisualizer.terrainRenderer = renderer;
            obstacleVisualizer.Bind(droneObj.GetComponent<DroneController>());

            var reseedVisualizer = terrainObj.AddComponent<ReseedMarkerVisualizer>();
            reseedVisualizer.terrainRenderer = renderer;
            reseedVisualizer.cellSize = cellSize;
            reseedVisualizer.Bind(droneObj.GetComponent<DroneController>());

            var animalVisualizer = terrainObj.AddComponent<AnimalDisturbanceVisualizer>();
            animalVisualizer.terrainRenderer = renderer;
            animalVisualizer.cellSize = cellSize;
            animalVisualizer.Bind(droneObj.GetComponent<DroneController>());

            return (obstacleVisualizer, reseedVisualizer);
        }

        private void BuildLight()
        {
            GameObject lightObj = new GameObject("Directional Light");
            var light = lightObj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            lightObj.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private void BuildCamera(GameObject droneObj)
        {
            GameObject camObj;
            Camera cam = Camera.main;
            if (cam != null)
            {
                camObj = cam.gameObject;
                // Strip any leftover camera-control components from
                // earlier attempts so they don't fight this one.
                var oldOrbit = camObj.GetComponent<TerrainOrbitCamera>();
                if (oldOrbit != null) Destroy(oldOrbit);
            }
            else
            {
                camObj = new GameObject("Main Camera");
                camObj.tag = "MainCamera";
                cam = camObj.AddComponent<Camera>();
            }
            cam.clearFlags = CameraClearFlags.Skybox;

            float zoneWorldSize = ARIA.Core.ARIAConstants.ZONE_SIZE * cellSize;
            float centreX = zoneWorldSize / 2f;

            float half = zoneWorldSize * 0.5f;
            var orbit = camObj.AddComponent<TerrainOrbitCamera>();
            orbit.target = new Vector3(centreX, 0f, half);
            orbit.dist = half * 1.333f;
            orbit.pitch = 30f;
            cam.farClipPlane = 3000f;

            orbit.followTarget = droneObj.transform;

            Debug.Log($"[SceneBootstrapper] Camera configured: TerrainOrbitCamera on '{camObj.name}', " +
                $"target={orbit.target}, dist={orbit.dist}, pitch={orbit.pitch}, following drone (damped). " +
                "If you don't see this exact line in the Console, BuildCamera() didn't run.");
        }

        private void BuildHud(GameObject droneObj, AerialObstacleVisualizer obstacleVisualizer)
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                GameObject esObj = new GameObject("EventSystem");
                esObj.AddComponent<EventSystem>();
                esObj.AddComponent<StandaloneInputModule>();
            }

            // ── Canvas ──
            GameObject canvasObj = new GameObject("HUD Canvas");
            var canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            canvasObj.AddComponent<GraphicRaycaster>();

            var hud = canvasObj.AddComponent<DroneHUD>();
            hud.obstacleVisualizer = obstacleVisualizer;
            hud.Bind(droneObj.GetComponent<DroneController>());
        }
    }
}
