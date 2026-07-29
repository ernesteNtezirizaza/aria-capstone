using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using ARIA.Core;
using ARIA.Systems;

namespace ARIA.Drone
{
    public class SeedTreeManager : MonoBehaviour
    {
        [Tooltip("Assign the same DroneController driving the episode.")]
        public DroneController drone;

        [Tooltip("Assign the RealTerrainRenderer so trees sit at the correct terrain height.")]
        public RealTerrainRenderer terrainRenderer;

        [Tooltip("World-space size of one terrain cell -- MUST match DroneController.cellSize.")]
        public float cellSize = 1.0f;

        [Tooltip("Real-world seconds to visually tween BETWEEN growth stage changes " +
                 "(pure animation smoothing -- the actual growth TIMING is real simulation steps).")]
        public float tweenDuration = 0.6f;

        [Tooltip("Minimum real-world seconds for the seed-drop fall animation " +
                 "(actual duration scales up from here based on fall height/fallSpeed).")]
        public float dropDuration = 0.5f;

        [Tooltip("World units/second a dropped seed falls -- duration scales with " +
                 "actual altitude so a drop from higher up visibly takes longer.")]
        public float fallSpeed = 14f;

        [Header("Planting hole & cover")]
        [Tooltip("Real-world seconds the seed spends sinking into the planting hole " +
                 "after it lands, before it's covered over.")]
        public float holeSinkDuration = 0.7f;

        [Tooltip("Real-world seconds for the soil mound to rise up and cover the hole.")]
        public float coverDuration = 0.8f;

        [Tooltip("Real-world seconds the covered mound sits undisturbed before the " +
                 "sprout emerges through it.")]
        public float coveredHoldTime = 1.5f;

        [Tooltip("DEPRECATED -- no longer used. Trees render at their exact grid position.")]
        public float jitterRange = 0f;

        private class TreeVisual
        {
            public GameObject SproutObject;  // small marker, used for Dropped/Germinating
            public GameObject TreeObject;    // real TreeBuilder mesh, used from Seedling onward
            public SeedStage  LastStage;
            public Coroutine  TweenRoutine;
            public Vector3    GridWorldPos;  // real, unjittered position (for spacing checks)
            public Vector3    RenderPos;     // jittered render position, reused for both objects
            public int        SpeciesId;
            public bool       IsSuitable;
            public float      TreeBaseScale = 1f; // RealTreeBuilder's per-species scale factor -- growth-stage tweening multiplies on top of this, not Vector3.one
        }

        private readonly Dictionary<int, TreeVisual> _visuals = new Dictionary<int, TreeVisual>();
        private readonly HashSet<int> _dropAnimating = new HashSet<int>();

        /* Only used from Seedling onward -- Dropped/Germinating use the sprout marker instead. */
        private static float TreeScale(SeedStage stage)
        {
            switch (stage)
            {
                case SeedStage.Seedling: return 0.55f;
                case SeedStage.Mature:   return 1.00f;
                case SeedStage.Dead:     return 0.35f;
                default:                 return 0.55f;
            }
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
            drone.OnEpisodeStarted += HandleNewEpisode;
        }

        private void Unsubscribe()
        {
            drone.OnStepTaken -= HandleStep;
            drone.OnEpisodeStarted -= HandleNewEpisode;
        }

        private int _lastSeenZoneIndex = -2;

        private void HandleNewEpisode(DroneController d)
        {
            bool zoneChanged = d.CurrentZoneIndex != _lastSeenZoneIndex;
            bool missionCompleteReset = d.LastEpisodeEndedByMissionComplete;

            if (zoneChanged || missionCompleteReset)
            {
                foreach (var v in _visuals.Values)
                {
                    if (v.SproutObject != null) Destroy(v.SproutObject);
                    if (v.TreeObject != null) Destroy(v.TreeObject);
                }
                _visuals.Clear();
                _dropAnimating.Clear();
                _lastSeenZoneIndex = d.CurrentZoneIndex;
            }
        }

        private void HandleStep(DroneController d)
        {
            foreach (var seed in d.State.Growth.Seeds.Values)
            {
                if (_visuals.TryGetValue(seed.SeedId, out var existing))
                {
                    if (existing.LastStage != seed.Stage)
                    {
                        if (existing.TweenRoutine != null) StopCoroutine(existing.TweenRoutine);
                        existing.TweenRoutine = StartCoroutine(TransitionTo(existing, seed.Stage));
                    }
                    continue;
                }
                if (_dropAnimating.Contains(seed.SeedId)) continue;

                _dropAnimating.Add(seed.SeedId);
                StartCoroutine(DropThenSprout(seed));
            }
        }

        private IEnumerator DropThenSprout(Seed seed)
        {
            float worldX = seed.X * cellSize;
            float worldZ = seed.Y * cellSize;
            float groundY = terrainRenderer != null ? terrainRenderer.GetHeight(seed.Y, seed.X) : 0f;
            Vector3 groundPos = new Vector3(worldX, groundY, worldZ);

            /* A genuine reseed lands a brand-new Seed (its own SeedId) at the
               exact same cell as an earlier one that already died there --
               that old, shrunken grey marker was otherwise left behind
               forever, sitting underneath/overlapping the new sprout as it
               grows. From a distance that read as "the seed marker moved",
               when really a second marker had appeared right on top of the
               first. Clear any dead marker occupying this exact cell before
               planting the new one. */
            int seedIdToRemove = -1;
            foreach (var kv in _visuals)
            {
                if (kv.Value.LastStage == SeedStage.Dead && kv.Value.GridWorldPos == groundPos)
                {
                    if (kv.Value.SproutObject != null) Destroy(kv.Value.SproutObject);
                    if (kv.Value.TreeObject != null) Destroy(kv.Value.TreeObject);
                    seedIdToRemove = kv.Key;
                    break;
                }
            }
            if (seedIdToRemove != -1) _visuals.Remove(seedIdToRemove);

            Vector3 startPos = drone != null ? drone.transform.position : groundPos + Vector3.up * 12f;

            var seedGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            seedGO.name = "SeedDrop";
            Destroy(seedGO.GetComponent<Collider>());
    
            Color speciesSeedColor = TreeBuilder.GetSeedColor(seed.SpeciesId);
            /* Was 0.5f, then 0.22f -- still read as an oversized ball rather
               than a seed at zone scale (cellSize=1 world unit per cell). */
            float seedSize = 0.1f * cellSize * TreeBuilder.GetSeedScale(seed.SpeciesId);
            seedGO.transform.localScale = Vector3.one * seedSize;
            seedGO.transform.position = startPos;
            var seedMat = MaterialHelper.GetDefaultMaterial();
            seedMat.color = speciesSeedColor;
            seedMat.EnableKeyword("_EMISSION");
            seedMat.SetColor("_EmissionColor", speciesSeedColor * 1.3f);
            seedGO.GetComponent<Renderer>().material = seedMat;

            var trail = seedGO.AddComponent<TrailRenderer>();
            trail.time = 0.3f;
            trail.startWidth = seedSize * 0.7f;
            trail.endWidth = 0.02f;
            trail.material = MaterialHelper.GetDefaultMaterial();
            trail.startColor = new Color(speciesSeedColor.r, speciesSeedColor.g, speciesSeedColor.b, 0.8f);
            trail.endColor = new Color(speciesSeedColor.r, speciesSeedColor.g, speciesSeedColor.b, 0f);

            /* Rest ON the surface, not centred AT it, or half the sphere clips through terrain. */
            Vector3 restingPos = groundPos + Vector3.up * (seedSize * 0.5f);

            float fallHeight = Mathf.Max(0.1f, startPos.y - restingPos.y);
            float duration = Mathf.Max(dropDuration, fallHeight / fallSpeed);

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                if (seedGO == null) { _dropAnimating.Remove(seed.SeedId); yield break; }
                float easedK = (t / duration) * (t / duration); // gravity-style ease
                seedGO.transform.position = Vector3.Lerp(startPos, restingPos, easedK);
                seedGO.transform.Rotate(Vector3.up, 360f * Time.deltaTime);
                yield return null;
            }

            var hole = SpawnHole(groundPos);

            /* Shrinks to a tiny nub without dipping below ground -- reads as sinking in. */
            Vector3 settledPos = groundPos + Vector3.up * (seedSize * 0.08f);
            float sinkT = 0f;
            while (sinkT < holeSinkDuration)
            {
                sinkT += Time.deltaTime;
                if (seedGO == null) break;
                float k = Mathf.Clamp01(sinkT / holeSinkDuration);
                seedGO.transform.position = Vector3.Lerp(restingPos, settledPos, k);
                seedGO.transform.localScale = Vector3.one * seedSize * Mathf.Lerp(1f, 0.12f, k);
                yield return null;
            }
            if (seedGO != null) Destroy(seedGO);

            var mound = SpawnSoilMound(groundPos, out Vector3 moundFullScale);
            float moundT = 0f;
            while (moundT < coverDuration)
            {
                moundT += Time.deltaTime;
                float k = Mathf.Clamp01(moundT / coverDuration);
                mound.transform.localScale = Vector3.Lerp(Vector3.zero, moundFullScale, k);
                yield return null;
            }

            yield return new WaitForSeconds(coveredHoldTime);

            _dropAnimating.Remove(seed.SeedId);

            if (_visuals.ContainsKey(seed.SeedId))
            {
                Destroy(hole);
                Destroy(mound);
                yield break;
            }

            var visual = SpawnSprout(seed, groundPos);
            _visuals[seed.SeedId] = visual;

            float settleDuration = tweenDuration * 0.5f;
            Vector3 moundStartScale = mound.transform.localScale;
            float settleT = 0f;
            while (settleT < settleDuration)
            {
                settleT += Time.deltaTime;
                float k = Mathf.Clamp01(settleT / settleDuration);
                mound.transform.localScale = Vector3.Lerp(moundStartScale, Vector3.zero, k);
                yield return null;
            }
            Destroy(mound);
            Destroy(hole);
        }

        private GameObject SpawnHole(Vector3 groundPos)
        {
            var container = new GameObject("PlantingHole");
            container.transform.position = groundPos;

            var pit = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pit.name = "Pit";
            Destroy(pit.GetComponent<Collider>());
            pit.transform.SetParent(container.transform, false);

            float pitWidth = 0.95f * cellSize;
            float pitHeight = 0.03f * cellSize;
            pit.transform.localScale = new Vector3(pitWidth, pitHeight * 0.5f, pitWidth);
            pit.transform.localPosition = Vector3.up * (pitHeight * 0.5f);

            var pitMat = MaterialHelper.GetDefaultMaterial();
            pitMat.color = new Color(0.03f, 0.02f, 0.015f); // near-black, reads as a shadowed pit
            pit.GetComponent<Renderer>().material = pitMat;

            const int clumpCount = 7;
            float rimRadius = pitWidth * 0.62f;
            for (int i = 0; i < clumpCount; i++)
            {
                float angle = (360f / clumpCount) * i + Random.Range(-15f, 15f);
                float rad = angle * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad)) * rimRadius;

                var clump = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                clump.name = "DirtClump";
                Destroy(clump.GetComponent<Collider>());
                clump.transform.SetParent(container.transform, false);

                float clumpSize = Random.Range(0.16f, 0.24f) * cellSize;
                clump.transform.localScale = Vector3.one * clumpSize;
                clump.transform.localPosition = offset + Vector3.up * (clumpSize * 0.35f);

                var clumpMat = MaterialHelper.GetDefaultMaterial();
                clumpMat.color = new Color(0.32f, 0.22f, 0.13f); // freshly turned soil
                clump.GetComponent<Renderer>().material = clumpMat;
            }

            return container;
        }

        private GameObject SpawnSoilMound(Vector3 groundPos, out Vector3 fullScale)
        {
            var mound = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            mound.name = "SoilMound";
            Destroy(mound.GetComponent<Collider>());

            float moundWidth = 0.5f * cellSize;
            float moundHeight = 0.16f * cellSize;
            fullScale = new Vector3(moundWidth, moundHeight, moundWidth);

            mound.transform.localScale = Vector3.zero;
            mound.transform.position = groundPos + Vector3.up * (moundHeight * 0.5f);

            var mat = MaterialHelper.GetDefaultMaterial();
            mat.color = new Color(0.36f, 0.26f, 0.16f); // loose, freshly-turned topsoil
            mound.GetComponent<Renderer>().material = mat;
            return mound;
        }

        private Vector3 ComputeRenderPos(Vector3 groundPos)
        {
            return groundPos;
        }

        private TreeVisual SpawnSprout(Seed seed, Vector3 groundPos)
        {
            Vector3 renderPos = ComputeRenderPos(groundPos);
            float speciesScale = TreeBuilder.GetSproutScale(seed.SpeciesId);

            var sprout = new GameObject($"Sprout_{seed.SeedId}");
            sprout.transform.position = renderPos;

            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "Stem";
            Destroy(stem.GetComponent<Collider>());
            stem.transform.SetParent(sprout.transform, false);
            stem.transform.localScale = new Vector3(0.04f, 0.12f * speciesScale, 0.04f);
            stem.transform.localPosition = new Vector3(0, 0.12f * speciesScale, 0);
            var stemMat = MaterialHelper.GetDefaultMaterial();
            stemMat.color = new Color(0.35f, 0.25f, 0.15f);
            stem.GetComponent<Renderer>().material = stemMat;

            var leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            leaf.name = "Leaf";
            Destroy(leaf.GetComponent<Collider>());
            leaf.transform.SetParent(sprout.transform, false);
            leaf.transform.localScale = Vector3.one * 0.18f * speciesScale;
            leaf.transform.localPosition = new Vector3(0, 0.26f * speciesScale, 0);
            var leafMat = MaterialHelper.GetDefaultMaterial();

            Color speciesTint = TreeBuilder.GetCanopyColor(seed.SpeciesId);
            leafMat.color = seed.IsSuitable
                ? speciesTint
                : Color.Lerp(speciesTint, Color.gray, 0.4f);
            leaf.GetComponent<Renderer>().material = leafMat;

            sprout.transform.localScale = Vector3.one * 0.4f; // starts tiny, grows toward 1

            return new TreeVisual
            {
                SproutObject = sprout,
                TreeObject = null,
                LastStage = seed.Stage,
                GridWorldPos = groundPos,
                RenderPos = renderPos,
                SpeciesId = seed.SpeciesId,
                IsSuitable = seed.IsSuitable,
            };
        }

        /* Real tree meshes (RealTreeBuilder) use glTFast's 'glTF/PbrMetallicRoughness'
           shader, which exposes neither "_Color" nor "_BaseColor" -- so an
           Material.color / HasProperty("_Color") check silently no-ops on them
           (confirmed live via RealTreeBuilder's own diagnostic). Enumerating the
           shader's actual declared properties finds whichever one is really a
           Color regardless of its name, so this works on both the real meshes
           and any plain procedural marker (sprouts, etc.) still using Standard. */
        private static void TintTowards(GameObject target, Color towardColor, float blend)
        {
            foreach (var rend in target.GetComponentsInChildren<Renderer>())
            {
                foreach (var mat in rend.materials)
                {
                    var shader = mat.shader;
                    int propCount = shader.GetPropertyCount();
                    for (int i = 0; i < propCount; i++)
                    {
                        if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
                        {
                            string propName = shader.GetPropertyName(i);
                            Color current = mat.GetColor(propName);
                            mat.SetColor(propName, Color.Lerp(current, towardColor, blend));
                        }
                    }
                }
            }
        }

        private IEnumerator TransitionTo(TreeVisual visual, SeedStage newStage)
        {
            /* Dead is its own branch below, so an early death just withers the sprout in place. */
            bool wasTreeStage = visual.LastStage == SeedStage.Seedling || visual.LastStage == SeedStage.Mature;
            bool isTreeStage  = newStage == SeedStage.Seedling || newStage == SeedStage.Mature;

            if (!wasTreeStage && isTreeStage)
            {
                if (visual.SproutObject != null) Destroy(visual.SproutObject);

                var tree = RealTreeBuilder.Build(visual.SpeciesId, existing: false);
                if (tree != null)
                {
                    visual.TreeBaseScale = tree.transform.localScale.x;
                    tree.transform.position = visual.RenderPos;
                    tree.transform.localScale = Vector3.one * (visual.TreeBaseScale * 0.15f); // starts small, tweens up below

                    if (!visual.IsSuitable)
                    {
                        TintTowards(tree, Color.gray, 0.4f);
                    }

                    visual.TreeObject = tree;
                }
            }

            visual.LastStage = newStage;

            GameObject target = visual.TreeObject != null ? visual.TreeObject : visual.SproutObject;
            if (target == null) yield break;

            float startScale = target.transform.localScale.x;
            float endScale;

            if (newStage == SeedStage.Dead)
            {
                /* Grey out and shrink the existing marker in place -- reads as "died here". */
                TintTowards(target, new Color(0.35f, 0.3f, 0.25f), 0.7f);
                endScale = startScale * 0.4f;
            }
            else
            {
                /* Tree stages scale relative to RealTreeBuilder's own
                   per-species base scale, not a flat 1.0 -- otherwise every
                   species would converge on the same final size regardless
                   of its intended scale factor. Sprouts have no such base
                   (still TreeBuilder's plain procedural marker) and keep
                   tweening toward their own full size of 1. */
                endScale = isTreeStage ? TreeScale(newStage) * visual.TreeBaseScale : 1f;
            }

            float t = 0f;
            while (t < tweenDuration)
            {
                t += Time.deltaTime;
                if (target == null) yield break;
                target.transform.localScale = Vector3.one * Mathf.Lerp(startScale, endScale, t / tweenDuration);
                yield return null;
            }
            if (target != null) target.transform.localScale = Vector3.one * endScale;
        }
    }
}
