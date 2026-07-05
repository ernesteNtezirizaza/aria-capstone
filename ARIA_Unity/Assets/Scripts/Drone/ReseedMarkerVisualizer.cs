using System.Collections.Generic;
using UnityEngine;
using ARIA.Core;

namespace ARIA.Drone
{
    public class ReseedMarkerVisualizer : MonoBehaviour
    {
        [Tooltip("Assign the same DroneController driving the episode.")]
        public DroneController drone;

        [Tooltip("Assign the RealTerrainRenderer so markers sit at the correct terrain height.")]
        public RealTerrainRenderer terrainRenderer;

        [Tooltip("World-space size of one terrain cell -- MUST match DroneController.cellSize.")]
        public float cellSize = 1.0f;

        private class Marker
        {
            public GameObject Go;
            public Material Mat;
        }

        private readonly Dictionary<(int y, int x), Marker> _markers = new Dictionary<(int, int), Marker>();

        public void Bind(DroneController d)
        {
            if (drone != null) { drone.OnStepTaken -= HandleStep; drone.OnEpisodeStarted -= HandleEpisodeStarted; }
            drone = d;
            if (drone != null) { drone.OnStepTaken += HandleStep; drone.OnEpisodeStarted += HandleEpisodeStarted; }
        }

        void OnDisable()
        {
            if (drone != null) { drone.OnStepTaken -= HandleStep; drone.OnEpisodeStarted -= HandleEpisodeStarted; }
        }

        private void HandleEpisodeStarted(DroneController d)
        {
            ClearAll();
        }

        private void HandleStep(DroneController d)
        {
            if (d.State == null) return;
            var live = d.State.ReseedingTargets;

            // Remove markers for anything no longer queued (just got
            // successfully replanted, or the episode moved on).
            List<(int y, int x)> toRemove = null;
            foreach (var key in _markers.Keys)
            {
                if (!live.Contains(key))
                {
                    (toRemove ??= new List<(int, int)>()).Add(key);
                }
            }
            if (toRemove != null)
                foreach (var key in toRemove)
                {
                    if (_markers[key].Go != null) Destroy(_markers[key].Go);
                    _markers.Remove(key);
                }

            // Add markers for anything newly queued.
            foreach (var t in live)
            {
                if (_markers.ContainsKey(t)) continue;
                _markers[t] = BuildMarker(t.y, t.x);
            }

            // Gentle pulse so the markers read as "needs attention"
            // rather than a static decal.
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 3f);
            foreach (var m in _markers.Values)
            {
                if (m.Mat != null)
                    m.Mat.SetColor("_EmissionColor", new Color(1.4f, 0.55f, 0.05f) * pulse);
            }
        }

        private Marker BuildMarker(int y, int x)
        {
            float worldX = x * cellSize;
            float worldZ = y * cellSize;
            float groundY = terrainRenderer != null ? terrainRenderer.GetHeight(y, x) : 0f;

            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = $"ReseedMarker_{y}_{x}";
            Destroy(go.GetComponent<Collider>());
            go.transform.position = new Vector3(worldX, groundY + 0.03f, worldZ);
            // Flat glowing ring/disc on the ground -- a beacon, not a
            // solid obstacle-looking shape.
            go.transform.localScale = new Vector3(1.1f * cellSize, 0.03f, 1.1f * cellSize);

            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(1f, 0.55f, 0.05f);
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(1.4f, 0.55f, 0.05f));
            go.GetComponent<Renderer>().material = mat;

            return new Marker { Go = go, Mat = mat };
        }

        private void ClearAll()
        {
            foreach (var m in _markers.Values)
                if (m.Go != null) Destroy(m.Go);
            _markers.Clear();
        }
    }
}
