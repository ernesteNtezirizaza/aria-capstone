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
        public float holeSinkDuration = 0.45f;

        [Tooltip("Real-world seconds for the soil mound to rise up and cover the hole.")]
        public float coverDuration = 0.5f;

        [Tooltip("Real-world seconds the covered mound sits undisturbed before the " +
                 "sprout emerges through it.")]
        public float coveredHoldTime = 0.7f;

        [Tooltip("DEPRECATED -- no longer used. Trees now always render at their exact " +
                 "real grid position (see ComputeRenderPos), by request: real " +
                 "reforestation plantings are laid out in precise, orderly rows, not " +
                 "randomly scattered, and the coverage sweep's spacing (see " +
                 "CoverageOverride.cs) already keeps real positions well clear of each " +
                 "other, so cosmetic jitter was no longer solving any actual overlap " +
                 "problem -- it was only ever making an already-correct grid look messier.")]
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
        }

        private readonly Dictionary<int, TreeVisual> _visuals = new Dictionary<int, TreeVisual>();
        private readonly HashSet<int> _dropAnimating = new HashSet<int>();

        // Real tree scale by stage -- only used from Seedling onward,
        // since Dropped/Germinating now use the separate sprout marker.
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

            Vector3 startPos = drone != null ? drone.transform.position : groundPos + Vector3.up * 12f;

            var seedGO = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            seedGO.name = "SeedDrop";
            Destroy(seedGO.GetComponent<Collider>());
    
            Color speciesSeedColor = TreeBuilder.GetSeedColor(seed.SpeciesId);
            float seedSize = 0.5f * cellSize * TreeBuilder.GetSeedScale(seed.SpeciesId);
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

            float fallHeight = Mathf.Max(0.1f, startPos.y - groundPos.y);
            float duration = Mathf.Max(dropDuration, fallHeight / fallSpeed);

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                if (seedGO == null) { _dropAnimating.Remove(seed.SeedId); yield break; }
                float easedK = (t / duration) * (t / duration); // gravity-style ease
                seedGO.transform.position = Vector3.Lerp(startPos, groundPos, easedK);
                seedGO.transform.Rotate(Vector3.up, 360f * Time.deltaTime);
                yield return null;
            }

            // ── Dig a hole and drop the seed into it ──────────────────
            var hole = SpawnHole(groundPos);

            float sinkDepth = 0.18f * cellSize;
            Vector3 holeBottom = groundPos + Vector3.down * sinkDepth;
            float sinkT = 0f;
            while (sinkT < holeSinkDuration)
            {
                sinkT += Time.deltaTime;
                if (seedGO == null) break;
                float k = Mathf.Clamp01(sinkT / holeSinkDuration);
                seedGO.transform.position = Vector3.Lerp(groundPos, holeBottom, k);
                seedGO.transform.localScale = Vector3.one * seedSize * Mathf.Lerp(1f, 0.4f, k);
                yield return null;
            }
            if (seedGO != null) Destroy(seedGO);

            // ── Cover the hole with soil ───────────────────────────────
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

            // ── Mound settles away as the sprout emerges through it ────
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
            var hole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            hole.name = "PlantingHole";
            Destroy(hole.GetComponent<Collider>());

            // The terrain is a flat plane at y=0 (RealTerrainRenderer.GetHeight always
            // returns 0), so sit this disc entirely ABOVE the surface rather than
            // straddling/embedding it -- otherwise most of its height is buried and
            // invisible.
            float holeWidth = 0.65f * cellSize;
            float holeHeight = 0.06f * cellSize;
            hole.transform.localScale = new Vector3(holeWidth, holeHeight * 0.5f, holeWidth);
            hole.transform.position = groundPos + Vector3.up * (holeHeight * 0.5f);

            var mat = MaterialHelper.GetDefaultMaterial();
            mat.color = new Color(0.18f, 0.12f, 0.08f); // dark, freshly-dug earth
            hole.GetComponent<Renderer>().material = mat;
            return hole;
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

        private IEnumerator TransitionTo(TreeVisual visual, SeedStage newStage)
        {
            bool wasTreeStage = visual.LastStage == SeedStage.Seedling
                              || visual.LastStage == SeedStage.Mature
                              || visual.LastStage == SeedStage.Dead;
            bool isTreeStage = newStage == SeedStage.Seedling
                             || newStage == SeedStage.Mature
                             || newStage == SeedStage.Dead;

            if (!wasTreeStage && isTreeStage)
            {
                if (visual.SproutObject != null) Destroy(visual.SproutObject);

                float baseHeight = 5f + visual.SpeciesId * 0.6f;
                var tree = TreeBuilder.Build(visual.SpeciesId, baseHeight, existing: false);
                tree.transform.position = visual.RenderPos;
                tree.transform.localScale = Vector3.one * 0.15f; // starts small, tweens up below

                if (!visual.IsSuitable)
                {
                    foreach (var rend in tree.GetComponentsInChildren<Renderer>())
                        rend.material.color = Color.Lerp(rend.material.color, Color.gray, 0.4f);
                }

                visual.TreeObject = tree;
            }

            visual.LastStage = newStage;

            if (newStage == SeedStage.Dead && visual.TreeObject != null)
            {
                foreach (var rend in visual.TreeObject.GetComponentsInChildren<Renderer>())
                    rend.material.color = Color.Lerp(rend.material.color, new Color(0.35f, 0.3f, 0.25f), 0.7f);
            }

            GameObject target = visual.TreeObject != null ? visual.TreeObject : visual.SproutObject;
            if (target == null) yield break;

            float startScale = target.transform.localScale.x;
            float endScale = isTreeStage ? TreeScale(newStage) : 1f; // sprout always tweens toward its own full size (1)

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
