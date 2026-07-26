using System.Collections.Generic;
using UnityEngine;
using ARIA.Core;

namespace ARIA.Drone
{
    /// Renders real obstacles as static, terrain-fixed hazards, matching
    /// how they actually work on the training side (env/rwanda_env.py):
    /// self.obs_grid is set once at episode reset and never modified
    /// during step() -- obstacles are permanent terrain features (steep
    /// slope + local elevation turbulence, see utils/preprocess.py's
    /// compute_obstacle()) that the drone discovers as it explores, not
    /// objects that move or hunt it.
    ///
    /// This replaces an earlier version that spawned fast-approaching
    /// spheres closing a 34-unit gap at 18 units/second -- visually
    /// dramatic, but it didn't correspond to anything the policy was
    /// actually trained against, which only ever sees a static per-cell
    /// hazard flag. A policy correctly avoiding a static hazard could
    /// still look like it's "not reacting" to a fast-incoming 3D object,
    /// because nothing resembling that ever appeared during training.
    public class AerialObstacleVisualizer : MonoBehaviour
    {
        [Tooltip("Assign the same DroneController driving the episode.")]
        public DroneController drone;

        [Tooltip("Assign the RealTerrainRenderer so markers sit on the real terrain surface, not a fixed height.")]
        public RealTerrainRenderer terrainRenderer;

        [Tooltip("World-space size of one terrain cell -- MUST match DroneController.cellSize.")]
        public float cellSize = 1.0f;

        [Tooltip("How far above the real terrain surface each marker sits, purely for visibility.")]
        public float markerLift = 1.5f;

        [Tooltip("Cap on how many hazard markers are actually drawn, biggest clusters first. A rugged " +
                 "zone can have hundreds of real hazard regions -- rendering all of them blankets the " +
                 "terrain and makes individual hazards impossible to pick out. The real hazard grid the " +
                 "policy navigates around is completely unaffected by this; it only limits what gets a " +
                 "visible marker, for legibility.")]
        public int maxMarkers = 15;

        [Tooltip("Optional: assign the imported 'Animated civilian Helicopter' prefab here to render " +
                 "hazards as this model (with its animation playing) instead of plain spheres. Left " +
                 "unassigned, markers fall back to the original sphere rendering.")]
        public GameObject helicopterPrefab;

        [Tooltip("Only used as a fallback if helicopterPrefab above is left empty -- loads " +
                 "Assets/Resources/<name>.prefab automatically. This component is added at runtime " +
                 "via SceneBootstrapper, not placed in the scene file, so there's no Inspector to " +
                 "drag a reference into -- same pattern as SceneBootstrapper's onnxResourceName.")]
        public string helicopterResourceName = "HelicopterHazard";

        [Tooltip("Cap on how many markers use the (much heavier, animated) helicopter model at once " +
                 "-- confirmed live that a rugged zone with hundreds of hazard clusters instantiating " +
                 "that many simultaneous animated meshes crashes the WebGL context. The largest " +
                 "clusters get the helicopter treatment; the rest still render as sphere markers, so " +
                 "nothing is hidden, it's purely a cost control on the fancier visual.")]
        public int maxHelicopterMarkers = 20;

        private readonly List<GameObject> _markers = new List<GameObject>();
        private bool _active;
        private bool _lastShownState = true;
        private bool _triedLoadingResource;

        public void Bind(DroneController d)
        {
            if (drone != null) drone.OnEpisodeStarted -= HandleEpisodeStarted;
            drone = d;
            if (drone != null) drone.OnEpisodeStarted += HandleEpisodeStarted;
        }

        void OnDisable()
        {
            if (drone != null) drone.OnEpisodeStarted -= HandleEpisodeStarted;
        }

        private void HandleEpisodeStarted(DroneController d)
        {
            RefreshMarkers();
        }

        public void RefreshMarkers()
        {
            ClearAllMarkers();

            if (helicopterPrefab == null && !_triedLoadingResource)
            {
                _triedLoadingResource = true;
                helicopterPrefab = Resources.Load<GameObject>(helicopterResourceName);
                if (helicopterPrefab == null)
                    Debug.Log($"[AerialObstacleVisualizer] No helicopter prefab found at " +
                        $"Resources/{helicopterResourceName} -- falling back to sphere markers. " +
                        "Convert Assets/Models/scene.gltf to a prefab and save it under " +
                        "Assets/Resources/ to enable it.");
            }

            // Real hazards are a static, always-present terrain feature (see
            // ActionDispatcher.Step()) -- always computed here regardless of
            // DemoConditions.ShowHazardMarkers, which only controls whether
            // the resulting markers are visible (see Update() below), never
            // whether the real hazard grid exists or blocks the drone.
            _active = drone != null && drone.State != null;
            if (!_active)
            {
                Debug.Log("[AerialObstacleVisualizer] No active drone/State -- 0 hazards shown.");
                return;
            }

            var zone = drone.State.Zone;
            var clusters = BuildClusters(zone);
            // Biggest hazards get the helicopter treatment first, once capped.
            clusters.Sort((a, b) => b.cellCount.CompareTo(a.cellCount));

            int placed = 0;
            foreach (var c in clusters)
            {
                if (placed >= maxMarkers) break;
                bool useHelicopter = placed < maxHelicopterMarkers;
                PlaceMarker(zone, c.centerX, c.centerY, c.cellCount, useHelicopter);
                placed++;
            }

            _lastShownState = DemoConditions.ShowHazardMarkers;
            SetMarkersVisible(_lastShownState);

            Debug.Log($"[AerialObstacleVisualizer] {clusters.Count} real hazard region(s) found, " +
                      $"{placed} marker(s) placed (static, matching the real obstacle grid the policy observes).");
        }

        void Update()
        {
            // Cheap per-frame check for the visibility toggle changing, so
            // it takes effect immediately rather than waiting for the next
            // episode reset -- mirrors the lightweight static-flag pattern
            // DemoConditions already uses elsewhere (no new event wiring).
            if (DemoConditions.ShowHazardMarkers != _lastShownState)
            {
                _lastShownState = DemoConditions.ShowHazardMarkers;
                SetMarkersVisible(_lastShownState);
            }
        }

        private void SetMarkersVisible(bool visible)
        {
            foreach (var m in _markers)
                if (m != null) m.SetActive(visible);
        }

        private struct Cluster
        {
            public int centerX, centerY, cellCount;
        }

        /// Groups adjacent obstacle cells (ObsGrid > OBSTACLE_THRESHOLD)
        /// into connected regions via flood fill, so one continuous
        /// hazardous slope renders as one marker sized to its real
        /// extent, not one marker per individual grid cell.
        private List<Cluster> BuildClusters(ZoneData zone)
        {
            int size = zone.Size;
            var visited = new bool[size, size];
            var clusters = new List<Cluster>();

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (visited[y, x]) continue;
                    if (zone.ObsGrid[y, x] <= ARIAConstants.OBSTACLE_THRESHOLD) continue;

                    // Flood fill this connected hazardous region.
                    var stack = new Stack<(int x, int y)>();
                    stack.Push((x, y));
                    visited[y, x] = true;
                    long sumX = 0, sumY = 0;
                    int count = 0;

                    while (stack.Count > 0)
                    {
                        var (cx, cy) = stack.Pop();
                        sumX += cx; sumY += cy; count++;

                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = cx + dx, ny = cy + dy;
                                if (nx < 0 || ny < 0 || nx >= size || ny >= size) continue;
                                if (visited[ny, nx]) continue;
                                if (zone.ObsGrid[ny, nx] <= ARIAConstants.OBSTACLE_THRESHOLD) continue;
                                visited[ny, nx] = true;
                                stack.Push((nx, ny));
                            }
                        }
                    }

                    clusters.Add(new Cluster {
                        centerX = Mathf.RoundToInt((float)sumX / count),
                        centerY = Mathf.RoundToInt((float)sumY / count),
                        cellCount = count,
                    });
                }
            }
            return clusters;
        }

        private void PlaceMarker(ZoneData zone, int gx, int gy, int cellCount, bool useHelicopter)
        {
            float worldX = gx * cellSize;
            float worldZ = gy * cellSize;
            float groundY = terrainRenderer != null ? terrainRenderer.GetHeight(gy, gx) : 0f;

            // Real physical extent: cellCount cells' worth of ground,
            // approximated as a circle, so a genuinely large hazardous
            // slope reads as visibly bigger than a single steep cell.
            float footprintCells = Mathf.Sqrt(cellCount);
            float visualDiameter = Mathf.Clamp(footprintCells * cellSize * 0.9f, cellSize * 1.5f, cellSize * 12f);

            GameObject go = (helicopterPrefab != null && useHelicopter)
                ? PlaceHelicopterMarker(worldX, groundY, worldZ, visualDiameter)
                : PlaceSphereMarker(worldX, groundY, worldZ, visualDiameter);

            _markers.Add(go);
        }

        private GameObject PlaceSphereMarker(float worldX, float groundY, float worldZ, float visualDiameter)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "RealTerrainHazard";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = new Vector3(worldX, groundY + markerLift, worldZ);
            go.transform.localScale = Vector3.one * visualDiameter;

            var mat = MaterialHelper.GetDefaultMaterial();
            mat.color = new Color(0.75f, 0.2f, 0.1f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.9f, 0.25f, 0.08f) * 0.6f); // steady glow, not flashing -- this is a fixed hazard, not an alarm
            go.GetComponent<Renderer>().material = mat;
            return go;
        }

        private GameObject PlaceHelicopterMarker(float worldX, float groundY, float worldZ, float visualDiameter)
        {
            var go = Instantiate(helicopterPrefab);
            go.name = "RealTerrainHazard (Helicopter)";
            // Imported model's own scale/orientation vary by asset -- normalise
            // against the same visualDiameter the sphere marker uses, so a
            // large hazard cluster still reads as visibly bigger than a small
            // one, consistent with the marker sizing this replaces.
            go.transform.position = new Vector3(worldX, groundY + markerLift + visualDiameter * 0.5f, worldZ);
            go.transform.localScale = Vector3.one * visualDiameter * 0.15f;

            foreach (var col in go.GetComponentsInChildren<Collider>())
                Destroy(col);

            // glTFast imports animation clips onto a Legacy Animation
            // component by default -- play it if present so the rotor
            // actually spins; harmless no-op if the asset has none.
            var anim = go.GetComponentInChildren<Animation>();
            if (anim != null && anim.clip != null)
            {
                anim.wrapMode = WrapMode.Loop;
                anim.Play();
            }
            else
            {
                var animator = go.GetComponentInChildren<Animator>();
                if (animator != null) animator.Play(0, 0, Random.value); // desync multiple instances
            }
            return go;
        }

        private void ClearAllMarkers()
        {
            foreach (var m in _markers)
                if (m != null) Destroy(m);
            _markers.Clear();
        }
    }
}
