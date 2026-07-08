using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ARIA.Core;
using ARIA.Systems;

namespace ARIA.Drone
{
    // Cosmetic goat layer: wanders near live seeds, plays an eating animation
    // when the real DisturbanceEngine kills one. Purely reactive, not authoritative.
    public class AnimalDisturbanceVisualizer : MonoBehaviour
    {
        [Tooltip("Assign the same DroneController driving the episode.")]
        public DroneController drone;

        [Tooltip("Assign the RealTerrainRenderer so goats sit at the correct terrain height.")]
        public RealTerrainRenderer terrainRenderer;

        [Tooltip("World-space size of one terrain cell -- MUST match DroneController.cellSize.")]
        public float cellSize = 1.0f;

        public int maxGoats = 4;
        public float wanderSpeed = 0.8f;
        public float wanderRadius = 1.6f;

        private class Goat
        {
            public GameObject Go;
            public Vector3 WanderCenter;
            public float RetargetTimer;
            public bool Eating;
        }

        private readonly List<Goat> _goats = new List<Goat>();
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
                if (_goats.Count > 0) ClearAll();
                return;
            }

            // Replay any disturbance kills that happened since we last looked.
            var events = d.State.Disturbance.Events;
            for (int i = _processedEventCount; i < events.Count; i++)
                TriggerEating(events[i]);
            _processedEventCount = events.Count;

            EnsureGoatCount();
        }

        void Update()
        {
            if (!_active || drone == null || drone.State == null) return;

            foreach (var goat in _goats)
            {
                if (goat.Go == null || goat.Eating) continue;
                Wander(goat);
            }
        }

        private void Wander(Goat goat)
        {
            goat.RetargetTimer -= Time.deltaTime;
            if (goat.RetargetTimer <= 0f) RetargetGoat(goat);

            int id = goat.Go.GetInstanceID();
            Vector3 noise = new Vector3(
                Mathf.PerlinNoise(Time.time * wanderSpeed, id * 0.017f) - 0.5f,
                0f,
                Mathf.PerlinNoise(id * 0.017f, Time.time * wanderSpeed) - 0.5f
            ) * wanderRadius * 2f;

            Vector3 target = goat.WanderCenter + noise;
            Vector3 pos = goat.Go.transform.position;
            Vector3 next = Vector3.MoveTowards(pos, target, wanderSpeed * cellSize * Time.deltaTime);
            Vector3 dir = next - pos;
            if (dir.sqrMagnitude > 0.0001f) goat.Go.transform.forward = dir.normalized;
            goat.Go.transform.position = next;
        }

        private void RetargetGoat(Goat goat)
        {
            goat.RetargetTimer = Random.Range(3f, 6f);
            var alive = drone.State.Growth.Alive();
            if (alive.Count == 0) return;
            var seed = PickWanderTarget(alive);
            goat.WanderCenter = GroundPos(seed.X, seed.Y);
        }

        // Prefer Seedling/Mature seeds (a real, visible tree mesh) over
        // Dropped/Germinating markers, which are too small to read clearly.
        private Seed PickWanderTarget(List<Seed> alive)
        {
            List<Seed> visible = null;
            foreach (var s in alive)
                if (s.Stage == SeedStage.Seedling || s.Stage == SeedStage.Mature) (visible ??= new List<Seed>()).Add(s);
            return visible != null ? visible[Random.Range(0, visible.Count)] : alive[Random.Range(0, alive.Count)];
        }

        private void EnsureGoatCount()
        {
            var alive = drone.State.Growth.Alive();
            if (alive.Count == 0) return;

            while (_goats.Count < maxGoats)
            {
                var seed = PickWanderTarget(alive);
                var go = BuildGoatVisual();
                var goat = new Goat
                {
                    Go = go,
                    WanderCenter = GroundPos(seed.X, seed.Y),
                    RetargetTimer = Random.Range(1f, 3f),
                };
                go.transform.position = goat.WanderCenter;
                _goats.Add(goat);
            }
        }

        private void TriggerEating(DisturbanceEvent e)
        {
            Vector3 seedPos = GroundPos(e.X, e.Y);

            // Reuse the nearest idle goat; spawn one on the spot if none are free.
            Goat chosen = null;
            float best = float.MaxValue;
            foreach (var goat in _goats)
            {
                if (goat.Eating || goat.Go == null) continue;
                float d = Vector3.SqrMagnitude(goat.Go.transform.position - seedPos);
                if (d < best) { best = d; chosen = goat; }
            }
            if (chosen == null)
            {
                var go = BuildGoatVisual();
                go.transform.position = seedPos;
                chosen = new Goat { Go = go };
                _goats.Add(chosen);
            }

            StartCoroutine(EatSeedRoutine(chosen, seedPos));
        }

        private IEnumerator EatSeedRoutine(Goat goat, Vector3 seedPos)
        {
            goat.Eating = true;
            Vector3 start = goat.Go.transform.position;
            const float moveDuration = 0.5f;
            float t = 0f;
            while (t < moveDuration)
            {
                t += Time.deltaTime;
                if (goat.Go == null) yield break;
                goat.Go.transform.position = Vector3.Lerp(start, seedPos, t / moveDuration);
                yield return null;
            }

            // Head-down munch -- a few big, slow dips/scale pulses so the "attack" reads
            // clearly instead of a subtle twitch that's easy to miss.
            Vector3 baseScale = goat.Go.transform.localScale;
            for (int i = 0; i < 4; i++)
            {
                if (goat.Go == null) yield break;
                goat.Go.transform.localScale = baseScale * 1.35f;
                yield return new WaitForSeconds(0.18f);
                goat.Go.transform.localScale = baseScale * 0.9f;
                yield return new WaitForSeconds(0.18f);
            }
            goat.Go.transform.localScale = baseScale;

            goat.Eating = false;
            RetargetGoat(goat);
        }

        private Vector3 GroundPos(int x, int y)
        {
            float worldX = x * cellSize;
            float worldZ = y * cellSize;
            float groundY = terrainRenderer != null ? terrainRenderer.GetHeight(y, x) : 0f;
            return new Vector3(worldX, groundY, worldZ);
        }

        // Real-world goat built from primitives: torso, head, snout, ears,
        // horns, chin beard, 4 legs, and a short tail.
        private GameObject BuildGoatVisual()
        {
            var root = new GameObject("Goat");

            float bodyLen = 1.8f * cellSize;
            float bodyWid = 0.70f * cellSize;
            float bodyHt  = 0.80f * cellSize;
            float legLen  = 0.85f * cellSize;
            float legRad  = 0.09f * cellSize;

            Color coat = new Color(0.07f, 0.07f, 0.08f); // black coat
            Color dark = new Color(0.03f, 0.03f, 0.03f); // near-black face/legs/horns

            float bodyCenterY = legLen + bodyHt * 0.5f;

            var body = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            body.name = "Body";
            body.transform.SetParent(root.transform, false);
            Destroy(body.GetComponent<Collider>());
            body.transform.localPosition = new Vector3(0, bodyCenterY, 0);
            body.transform.localScale = new Vector3(bodyWid, bodyHt, bodyLen);
            SetGoatMat(body, coat);

            Vector3 headPos = new Vector3(0, bodyCenterY + bodyHt * 0.25f, bodyLen * 0.58f);
            var head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(root.transform, false);
            Destroy(head.GetComponent<Collider>());
            head.transform.localPosition = headPos;
            head.transform.localScale = Vector3.one * bodyWid * 0.62f;
            SetGoatMat(head, coat * 1.05f);

            var snout = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            snout.name = "Snout";
            snout.transform.SetParent(root.transform, false);
            Destroy(snout.GetComponent<Collider>());
            snout.transform.localPosition = headPos + new Vector3(0, -bodyWid * 0.08f, bodyWid * 0.28f);
            snout.transform.localScale = new Vector3(bodyWid * 0.32f, bodyWid * 0.24f, bodyWid * 0.34f);
            SetGoatMat(snout, dark);

            float headSize = bodyWid * 0.62f;
            AddEar(root, headPos, headSize, -1);
            AddEar(root, headPos, headSize, 1);
            AddAppendage(root, headPos + Vector3.up * bodyWid * 0.2f, new Vector3(-35, 10, 0), bodyHt * 0.35f, legRad * 0.7f, dark);
            AddAppendage(root, headPos + Vector3.up * bodyWid * 0.2f, new Vector3(-35, -10, 0), bodyHt * 0.35f, legRad * 0.7f, dark);
            AddAppendage(root, headPos + new Vector3(0, -bodyWid * 0.25f, bodyWid * 0.2f), new Vector3(160, 0, 0), bodyHt * 0.2f, legRad * 0.5f, dark);

            float[] legZ = { bodyLen * 0.32f, -bodyLen * 0.32f };
            foreach (float z in legZ)
            {
                AddLeg(root,  bodyWid * 0.38f, z, bodyCenterY - bodyHt * 0.3f, legLen, legRad, dark);
                AddLeg(root, -bodyWid * 0.38f, z, bodyCenterY - bodyHt * 0.3f, legLen, legRad, dark);
            }

            AddAppendage(root, new Vector3(0, bodyCenterY + bodyHt * 0.2f, -bodyLen * 0.52f), new Vector3(-50, 0, 0), bodyHt * 0.4f, legRad * 0.8f, coat);

            return root;
        }

        private void AddEar(GameObject parent, Vector3 headPos, float headSize, int side)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Ear";
            go.transform.SetParent(parent.transform, false);
            Destroy(go.GetComponent<Collider>());
            go.transform.localPosition = headPos + new Vector3(side * headSize * 0.65f, headSize * 0.08f, -headSize * 0.08f);
            go.transform.localRotation = Quaternion.Euler(0, 0, side * 40f);
            go.transform.localScale = new Vector3(headSize * 0.5f, headSize * 0.18f, headSize * 0.36f);
            SetGoatMat(go, new Color(0.07f, 0.07f, 0.08f));
        }

        // Vertical leg from the body down to the ground.
        private void AddLeg(GameObject parent, float x, float z, float topY, float length, float radius, Color col)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Leg";
            go.transform.SetParent(parent.transform, false);
            Destroy(go.GetComponent<Collider>());
            go.transform.localPosition = new Vector3(x, topY - length * 0.5f, z);
            go.transform.localScale = new Vector3(radius, length * 0.5f, radius);
            SetGoatMat(go, col);
        }

        // Angled thin appendage (horn/beard/tail) anchored at a point and swept along its own axis.
        private void AddAppendage(GameObject parent, Vector3 pos, Vector3 eulerAngles, float length, float radius, Color col)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "Appendage";
            go.transform.SetParent(parent.transform, false);
            Destroy(go.GetComponent<Collider>());
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(eulerAngles) * Quaternion.Euler(90, 0, 0);
            go.transform.localScale = new Vector3(radius, length * 0.5f, radius);
            go.transform.Translate(Vector3.up * (length * 0.5f), Space.Self);
            SetGoatMat(go, col);
        }

        private void SetGoatMat(GameObject go, Color col)
        {
            var rend = go.GetComponent<Renderer>();
            if (rend == null) return;
            var mat = MaterialHelper.GetDefaultMaterial();
            mat.color = col;
            mat.EnableKeyword("_EMISSION");
            // Fixed rim tint rather than a multiply -- a near-black coat would
            // otherwise emit almost nothing and disappear against the terrain again.
            mat.SetColor("_EmissionColor", col * 0.9f + new Color(0.10f, 0.10f, 0.12f));
            rend.material = mat;
        }

        private void ClearAll()
        {
            foreach (var goat in _goats)
                if (goat.Go != null) Destroy(goat.Go);
            _goats.Clear();
        }
    }
}
