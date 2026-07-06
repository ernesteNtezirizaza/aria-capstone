using System.Collections.Generic;
using UnityEngine;
using ARIA.Core;

namespace ARIA.Drone
{
    public class AerialObstacleVisualizer : MonoBehaviour
    {
        [Tooltip("Assign the same DroneController driving the episode.")]
        public DroneController drone;

        [Tooltip("World-space size of one terrain cell -- MUST match DroneController.cellSize.")]
        public float cellSize = 1.0f;

        [Tooltip("How high above the ground these hazards fly -- set to match the " +
                 "drone's typical cruise altitude (altitudeWorldScale * 1.0) so they're " +
                 "directly in its visible flight path, not off to the side.")]
        public float hoverHeight = 12.0f; // matches DroneController's new altitudeWorldScale (12) at full altitude

        [Tooltip("How many hazards are incoming at once.")]
        public int maxActiveObstacles = 3;

        [Tooltip("World units/second an incoming hazard closes on the drone -- fast " +
                 "by request, so it clearly reads as something to react to, not a " +
                 "slow drift.")]
        public float approachSpeed = 18.0f;

        [Tooltip("Spawn distance (world units) from the drone's current position.")]
        public float spawnDistance = 34.0f;

        [Tooltip("Once a hazard gets this close (world units) to the drone, or flies " +
                 "past it, it's retired and a fresh one spawns from a new direction.")]
        public float retireDistance = 4.0f;

        [Tooltip("How many extra cells out from centre this hazard occupies on ObsGrid " +
                 "in every direction -- 2 means a 5x5 block of cells is 'hot' at once, " +
                 "matching a genuinely BIG obstacle instead of a single hidden cell.")]
        public int footprintRadius = 2;

        private class Hazard
        {
            public GameObject Go;
            public TrailRenderer Trail;
            public readonly List<(int x, int y)> OwnedCells = new List<(int, int)>();
            public int CenterX = int.MinValue, CenterY = int.MinValue;
        }

        private readonly List<Hazard> _hazards = new List<Hazard>();
        private System.Random _rng = new System.Random();
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
            ClearAllHazards();

            _active = drone != null && drone.State != null && DemoConditions.ObstacleOverlayEnabled;
            if (!_active)
            {
                Debug.Log("[AerialObstacleVisualizer] Obstacles off or no active drone/State -- 0 hazards flying.");
                return;
            }

            for (int i = 0; i < maxActiveObstacles; i++)
                SpawnHazard();

            Debug.Log($"[AerialObstacleVisualizer] Obstacles on -- {_hazards.Count} big aerial hazard(s) now incoming.");
        }

        void Update()
        {
            if (!_active || drone == null || drone.State == null) return;

            Vector3 dronePos = drone.transform.position;

            for (int i = _hazards.Count - 1; i >= 0; i--)
            {
                var h = _hazards[i];
                if (h.Go == null) { _hazards.RemoveAt(i); continue; }

                Vector3 pos = h.Go.transform.position;
                Vector3 toDrone = dronePos - pos;
                toDrone.y = 0f;
                float dist = toDrone.magnitude;

                if (dist <= retireDistance)
                {
                    ClearHazardCells(h);
                    Destroy(h.Go);
                    _hazards.RemoveAt(i);
                    SpawnHazard();
                    continue;
                }

                Vector3 dir = toDrone.normalized;
                pos += dir * approachSpeed * Time.deltaTime;
                pos.y = hoverHeight;
                h.Go.transform.position = pos;
                h.Go.transform.forward = dir;

                UpdateHazardCells(h, pos);
            }
        }

        private void SpawnHazard()
        {
            if (drone == null || drone.State == null) return;

            Vector3 dronePos = drone.transform.position;
            float angle = (float)(_rng.NextDouble() * Mathf.PI * 2.0);
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spawnDistance;
            Vector3 spawnPos = dronePos + offset;
            spawnPos.y = hoverHeight;

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "IncomingAerialObstacle";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = spawnPos;

            float visualDiameter = (footprintRadius * 2 + 1) * cellSize * 0.85f;
            go.transform.localScale = Vector3.one * visualDiameter;

            var mat = MaterialHelper.GetDefaultMaterial();
            mat.color = new Color(1f, 0.12f, 0.05f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(1.4f, 0.18f, 0.05f)); // bright glow, visible from a distance
            go.GetComponent<Renderer>().material = mat;

            var trail = go.AddComponent<TrailRenderer>();
            trail.time = 0.5f;
            trail.startWidth = visualDiameter * 0.6f;
            trail.endWidth = 0.05f;
            trail.material = MaterialHelper.GetDefaultMaterial();
            trail.startColor = new Color(1f, 0.3f, 0.1f, 0.75f);
            trail.endColor = new Color(1f, 0.3f, 0.1f, 0f);

            var h = new Hazard { Go = go, Trail = trail };
            UpdateHazardCells(h, spawnPos);
            _hazards.Add(h);
        }

        private void UpdateHazardCells(Hazard h, Vector3 worldPos)
        {
            if (drone.State == null) return;
            var zone = drone.State.Zone;
            int cx = Mathf.Clamp(Mathf.RoundToInt(worldPos.x / cellSize), 0, zone.Size - 1);
            int cy = Mathf.Clamp(Mathf.RoundToInt(worldPos.z / cellSize), 0, zone.Size - 1);

            if (h.CenterX == cx && h.CenterY == cy) return; // hasn't crossed into a new centre cell yet

            ClearHazardCells(h);
            h.CenterX = cx;
            h.CenterY = cy;

            for (int oy = -footprintRadius; oy <= footprintRadius; oy++)
            {
                for (int ox = -footprintRadius; ox <= footprintRadius; ox++)
                {
                    int gx = Mathf.Clamp(cx + ox, 0, zone.Size - 1);
                    int gy = Mathf.Clamp(cy + oy, 0, zone.Size - 1);
                    zone.ObsGrid[gy, gx] = 0.95f; // safely above OBSTACLE_THRESHOLD (0.7)
                    h.OwnedCells.Add((gx, gy));
                }
            }
        }

        private void ClearHazardCells(Hazard h)
        {
            if (drone == null || drone.State == null) { h.OwnedCells.Clear(); return; }
            var zone = drone.State.Zone;
            foreach (var (gx, gy) in h.OwnedCells)
                zone.ObsGrid[gy, gx] = 0f;
            h.OwnedCells.Clear();
        }

        private void ClearAllHazards()
        {
            foreach (var h in _hazards)
            {
                ClearHazardCells(h);
                if (h.Go != null) Destroy(h.Go);
            }
            _hazards.Clear();
        }
    }
}
