using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ARIA.Core;
using ARIA.Systems;

namespace ARIA.Drone
{
    // Cosmetic insect layer for the "Animal Disturbance" demo toggle. Insects
    // wander near live seeds and play an eating animation when the real
    // DisturbanceEngine kills a seed -- DisturbanceEngine stays the sole
    // authority on which seeds actually die, this only reacts to it.
    public class AnimalDisturbanceVisualizer : MonoBehaviour
    {
        [Tooltip("Assign the same DroneController driving the episode.")]
        public DroneController drone;

        [Tooltip("Assign the RealTerrainRenderer so insects sit at the correct terrain height.")]
        public RealTerrainRenderer terrainRenderer;

        [Tooltip("World-space size of one terrain cell -- MUST match DroneController.cellSize.")]
        public float cellSize = 1.0f;

        public int maxInsects = 5;
        public float wanderSpeed = 1.0f;
        public float wanderRadius = 1.2f;

        private class Insect
        {
            public GameObject Go;
            public Vector3 WanderCenter;
            public float RetargetTimer;
            public bool Eating;
        }

        private readonly List<Insect> _insects = new List<Insect>();
        private int _processedEventCount;
        private bool _active;

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
            _processedEventCount = 0;
        }

        private void HandleStep(DroneController d)
        {
            if (d.State == null) return;

            _active = DemoConditions.AnimalDisturbanceEnabled;
            if (!_active)
            {
                if (_insects.Count > 0) ClearAll();
                return;
            }

            // Replay any disturbance kills that happened since we last looked.
            var events = d.State.Disturbance.Events;
            for (int i = _processedEventCount; i < events.Count; i++)
                TriggerEating(events[i]);
            _processedEventCount = events.Count;

            EnsureInsectCount();
        }

        void Update()
        {
            if (!_active || drone == null || drone.State == null) return;

            foreach (var insect in _insects)
            {
                if (insect.Go == null || insect.Eating) continue;
                Wander(insect);
            }
        }

        private void Wander(Insect insect)
        {
            insect.RetargetTimer -= Time.deltaTime;
            if (insect.RetargetTimer <= 0f) RetargetInsect(insect);

            int id = insect.Go.GetInstanceID();
            Vector3 noise = new Vector3(
                Mathf.PerlinNoise(Time.time * wanderSpeed, id * 0.017f) - 0.5f,
                0f,
                Mathf.PerlinNoise(id * 0.017f, Time.time * wanderSpeed) - 0.5f
            ) * wanderRadius * 2f;

            Vector3 target = insect.WanderCenter + noise;
            Vector3 pos = insect.Go.transform.position;
            Vector3 next = Vector3.MoveTowards(pos, target, wanderSpeed * cellSize * Time.deltaTime);
            Vector3 dir = next - pos;
            if (dir.sqrMagnitude > 0.0001f) insect.Go.transform.forward = dir.normalized;
            insect.Go.transform.position = next;
        }

        private void RetargetInsect(Insect insect)
        {
            insect.RetargetTimer = Random.Range(2f, 5f);
            var living = drone.State.Growth.Living();
            if (living.Count == 0) return;
            var seed = living[Random.Range(0, living.Count)];
            insect.WanderCenter = GroundPos(seed.X, seed.Y) + Vector3.up * 0.03f;
        }

        private void EnsureInsectCount()
        {
            var living = drone.State.Growth.Living();
            if (living.Count == 0) return;

            while (_insects.Count < maxInsects)
            {
                var seed = living[Random.Range(0, living.Count)];
                var go = BuildInsectVisual();
                var insect = new Insect
                {
                    Go = go,
                    WanderCenter = GroundPos(seed.X, seed.Y) + Vector3.up * 0.03f,
                    RetargetTimer = Random.Range(1f, 3f),
                };
                go.transform.position = insect.WanderCenter;
                _insects.Add(insect);
            }
        }

        private void TriggerEating(DisturbanceEvent e)
        {
            Vector3 seedPos = GroundPos(e.X, e.Y) + Vector3.up * 0.03f;

            // Reuse the nearest idle insect; spawn one on the spot if none are free.
            Insect chosen = null;
            float best = float.MaxValue;
            foreach (var insect in _insects)
            {
                if (insect.Eating || insect.Go == null) continue;
                float d = Vector3.SqrMagnitude(insect.Go.transform.position - seedPos);
                if (d < best) { best = d; chosen = insect; }
            }
            if (chosen == null)
            {
                var go = BuildInsectVisual();
                go.transform.position = seedPos;
                chosen = new Insect { Go = go };
                _insects.Add(chosen);
            }

            StartCoroutine(EatSeedRoutine(chosen, seedPos));
        }

        private IEnumerator EatSeedRoutine(Insect insect, Vector3 seedPos)
        {
            insect.Eating = true;
            Vector3 start = insect.Go.transform.position;
            const float moveDuration = 0.35f;
            float t = 0f;
            while (t < moveDuration)
            {
                t += Time.deltaTime;
                if (insect.Go == null) yield break;
                insect.Go.transform.position = Vector3.Lerp(start, seedPos, t / moveDuration);
                yield return null;
            }

            // Chomp -- a few quick scale pulses on the seed's spot.
            for (int i = 0; i < 3; i++)
            {
                if (insect.Go == null) yield break;
                insect.Go.transform.localScale = Vector3.one * 1.25f;
                yield return new WaitForSeconds(0.08f);
                insect.Go.transform.localScale = Vector3.one;
                yield return new WaitForSeconds(0.08f);
            }

            insect.Eating = false;
            RetargetInsect(insect);
        }

        private Vector3 GroundPos(int x, int y)
        {
            float worldX = x * cellSize;
            float worldZ = y * cellSize;
            float groundY = terrainRenderer != null ? terrainRenderer.GetHeight(y, x) : 0f;
            return new Vector3(worldX, groundY, worldZ);
        }

        // Builds a small beetle/ant-like insect from primitives: oval
        // abdomen, head, antennae, and three pairs of splayed thin legs.
        private GameObject BuildInsectVisual()
        {
            var root = new GameObject("Insect");

            float bodyLen = 0.34f * cellSize;
            float bodyWid = 0.16f * cellSize;
            float bodyHt  = 0.12f * cellSize;
            Color shell = new Color(0.10f, 0.07f, 0.04f); // near-black carapace

            var abdomen = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            abdomen.name = "Abdomen";
            abdomen.transform.SetParent(root.transform, false);
            Destroy(abdomen.GetComponent<Collider>());
            abdomen.transform.localPosition = new Vector3(0, bodyHt * 0.5f, -bodyLen * 0.15f);
            abdomen.transform.localScale = new Vector3(bodyWid, bodyHt, bodyLen * 0.7f);
            SetInsectMat(abdomen, shell);

            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            Destroy(head.GetComponent<Collider>());
            Vector3 headPos = new Vector3(0, bodyHt * 0.5f, bodyLen * 0.42f);
            head.transform.localPosition = headPos;
            head.transform.localScale = Vector3.one * bodyWid * 0.8f;
            SetInsectMat(head, shell * 1.15f);

            AddThinLimb(root, headPos, new Vector3(15, 20, 0), bodyLen * 0.4f, bodyWid * 0.06f, shell);
            AddThinLimb(root, headPos, new Vector3(15, -20, 0), bodyLen * 0.4f, bodyWid * 0.06f, shell);

            float[] legZ = { bodyLen * 0.25f, 0f, -bodyLen * 0.22f };
            foreach (float z in legZ)
            {
                Vector3 anchor = new Vector3(0, bodyHt * 0.5f, z);
                AddThinLimb(root, anchor + Vector3.right * bodyWid * 0.4f, new Vector3(0, 0, -55), bodyLen * 0.32f, bodyWid * 0.05f, shell);
                AddThinLimb(root, anchor + Vector3.left  * bodyWid * 0.4f, new Vector3(0, 0,  55), bodyLen * 0.32f, bodyWid * 0.05f, shell);
            }

            return root;
        }

        private void AddThinLimb(GameObject parent, Vector3 pos, Vector3 eulerAngles, float length, float radius, Color col)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Limb";
            go.transform.SetParent(parent.transform, false);
            Destroy(go.GetComponent<Collider>());
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(eulerAngles) * Quaternion.Euler(90, 0, 0);
            go.transform.localScale = new Vector3(radius, length * 0.5f, radius);
            go.transform.Translate(Vector3.up * (length * 0.5f), Space.Self);
            SetInsectMat(go, col * 0.9f);
        }

        private void SetInsectMat(GameObject go, Color col)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend == null) return;
            var mat = MaterialHelper.GetDefaultMaterial();
            mat.color = col;
            rend.material = mat;
        }

        private void ClearAll()
        {
            foreach (var insect in _insects)
                if (insect.Go != null) Destroy(insect.Go);
            _insects.Clear();
        }
    }
}
