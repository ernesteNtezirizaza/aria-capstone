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

        [Tooltip("Safety cap on how many individual hazard markers to instantiate, in case a zone " +
                 "has a very large contiguous hazardous region -- adjacent hazard cells are merged " +
                 "into clusters first (see BuildClusters), so this should rarely bind in practice.")]
        public int maxMarkers = 400;

        private readonly List<GameObject> _markers = new List<GameObject>();
        private bool _active;

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

            _active = drone != null && drone.State != null && DemoConditions.ObstacleOverlayEnabled;
            if (!_active)
            {
                Debug.Log("[AerialObstacleVisualizer] Obstacles off or no active drone/State -- 0 hazards shown.");
                return;
            }

            var zone = drone.State.Zone;
            var clusters = BuildClusters(zone);

            int placed = 0;
            foreach (var c in clusters)
            {
                if (placed >= maxMarkers) break;
                PlaceMarker(zone, c.centerX, c.centerY, c.cellCount);
                placed++;
            }

            Debug.Log($"[AerialObstacleVisualizer] Obstacles on -- {clusters.Count} real hazard region(s) found, " +
                      $"{placed} marker(s) placed (static, matching the real obstacle grid the policy observes).");
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

        private void PlaceMarker(ZoneData zone, int gx, int gy, int cellCount)
        {
            float worldX = gx * cellSize;
            float worldZ = gy * cellSize;
            float groundY = terrainRenderer != null ? terrainRenderer.GetHeight(gy, gx) : 0f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "RealTerrainHazard";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = new Vector3(worldX, groundY + markerLift, worldZ);

            // Real physical extent: cellCount cells' worth of ground,
            // approximated as a circle, so a genuinely large hazardous
            // slope reads as visibly bigger than a single steep cell.
            float footprintCells = Mathf.Sqrt(cellCount);
            float visualDiameter = Mathf.Clamp(footprintCells * cellSize * 0.9f, cellSize * 1.5f, cellSize * 12f);
            go.transform.localScale = Vector3.one * visualDiameter;

            var mat = MaterialHelper.GetDefaultMaterial();
            mat.color = new Color(0.75f, 0.2f, 0.1f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.9f, 0.25f, 0.08f) * 0.6f); // steady glow, not flashing -- this is a fixed hazard, not an alarm
            go.GetComponent<Renderer>().material = mat;

            _markers.Add(go);
        }

        private void ClearAllMarkers()
        {
            foreach (var m in _markers)
                if (m != null) Destroy(m);
            _markers.Clear();
        }
    }
}
