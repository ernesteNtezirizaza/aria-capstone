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
    
            float seedSize = 0.5f * cellSize;
            seedGO.transform.localScale = Vector3.one * seedSize;
            seedGO.transform.position = startPos;
            var seedMat = new Material(Shader.Find("Standard"));
            seedMat.color = new Color(0.95f, 0.85f, 0.2f);
            seedMat.EnableKeyword("_EMISSION");
            seedMat.SetColor("_EmissionColor", new Color(1.2f, 1.0f, 0.2f));
            seedGO.GetComponent<Renderer>().material = seedMat;

            var trail = seedGO.AddComponent<TrailRenderer>();
            trail.time = 0.3f;
            trail.startWidth = seedSize * 0.7f;
            trail.endWidth = 0.02f;
            trail.material = new Material(Shader.Find("Standard"));
            trail.startColor = new Color(1f, 0.9f, 0.3f, 0.8f);
            trail.endColor = new Color(1f, 0.9f, 0.3f, 0f);

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
            if (seedGO != null) Destroy(seedGO);
            _dropAnimating.Remove(seed.SeedId);

            if (_visuals.ContainsKey(seed.SeedId)) yield break;

            var visual = SpawnSprout(seed, groundPos);
            _visuals[seed.SeedId] = visual;
        }

        private Vector3 ComputeRenderPos(Vector3 groundPos)
        {
            return groundPos;
        }

        private TreeVisual SpawnSprout(Seed seed, Vector3 groundPos)
        {
            Vector3 renderPos = ComputeRenderPos(groundPos);

            var sprout = new GameObject($"Sprout_{seed.SeedId}");
            sprout.transform.position = renderPos;

            var stem = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stem.name = "Stem";
            Destroy(stem.GetComponent<Collider>());
            stem.transform.SetParent(sprout.transform, false);
            stem.transform.localScale = new Vector3(0.04f, 0.12f, 0.04f);
            stem.transform.localPosition = new Vector3(0, 0.12f, 0);
            var stemMat = new Material(Shader.Find("Standard"));
            stemMat.color = new Color(0.35f, 0.25f, 0.15f);
            stem.GetComponent<Renderer>().material = stemMat;

            var leaf = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            leaf.name = "Leaf";
            Destroy(leaf.GetComponent<Collider>());
            leaf.transform.SetParent(sprout.transform, false);
            leaf.transform.localScale = Vector3.one * 0.18f;
            leaf.transform.localPosition = new Vector3(0, 0.26f, 0);
            var leafMat = new Material(Shader.Find("Standard"));
            
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
