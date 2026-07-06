using System.Collections.Generic;
using UnityEngine;
using Unity.InferenceEngine;
using ARIA.Core;

namespace ARIA.ML
{
    public class ARIAPolicyInference : MonoBehaviour
    {
        [Tooltip("Drag aria_policy.onnx here once Unity has imported it as a ModelAsset.")]
        public ModelAsset onnxModelAsset;

        private Model  _runtimeModel;
        private Worker _worker;
        private bool   _initialised;

        void Awake()
        {
            Initialise();
        }

        public void Initialise()
        {
            if (onnxModelAsset == null)
            {
                Debug.LogError("[ARIAPolicyInference] No ONNX ModelAsset assigned. " +
                    "Drag aria_policy.onnx into the onnxModelAsset field in the Inspector.");
                return;
            }

            _runtimeModel = ModelLoader.Load(onnxModelAsset);
            // WebGL's compute shader support is inconsistent across browsers/hardware
            // (missing kernels have been observed there), so use the CPU backend for
            // reliability instead of GPUCompute.
            _worker = new Worker(_runtimeModel, BackendType.CPU);
            _initialised = true;
        }

        void OnDestroy()
        {
            _worker?.Dispose();
        }

        public float[] Infer(Observation obs)
        {
            if (!_initialised)
            {
                Debug.LogError("[ARIAPolicyInference] Infer() called before Initialise() succeeded.");
                return new float[ARIAConstants.N_ACTIONS];
            }

            using var terrainWindow  = new Tensor<float>(
                new TensorShape(1, ARIAConstants.OBS_WINDOW, ARIAConstants.OBS_WINDOW, ARIAConstants.N_CHANNELS),
                obs.TerrainWindow);
            using var droneVector    = new Tensor<float>(new TensorShape(1, 10), obs.DroneVector);
            using var coverageMap    = new Tensor<float>(
                new TensorShape(1, ARIAConstants.ZONE_SIZE, ARIAConstants.ZONE_SIZE, 1), obs.CoverageMap);
            using var lifecycleMap   = new Tensor<float>(
                new TensorShape(1, ARIAConstants.ZONE_SIZE, ARIAConstants.ZONE_SIZE, 1), obs.LifecycleMap);
            using var disturbanceMap = new Tensor<float>(
                new TensorShape(1, ARIAConstants.ZONE_SIZE, ARIAConstants.ZONE_SIZE, 1), obs.DisturbanceMap);
            using var obstacleMap    = new Tensor<float>(
                new TensorShape(1, ARIAConstants.ZONE_SIZE, ARIAConstants.ZONE_SIZE, 1), obs.ObstacleMap);
            using var missionVector  = new Tensor<float>(new TensorShape(1, 8), obs.MissionVector);
            using var terrainStats   = new Tensor<float>(new TensorShape(1, 6), obs.TerrainStats);

            var inputs = new Dictionary<string, Tensor>
            {
                { "terrain_window",  terrainWindow },
                { "drone_vector",    droneVector },
                { "coverage_map",    coverageMap },
                { "lifecycle_map",   lifecycleMap },
                { "disturbance_map", disturbanceMap },
                { "obstacle_map",    obstacleMap },
                { "mission_vector",  missionVector },
                { "terrain_stats",   terrainStats },
            };

            foreach (var kv in inputs)
                _worker.SetInput(kv.Key, kv.Value);

            _worker.Schedule();

            using var output = _worker.PeekOutput("action_logits") as Tensor<float>;
            float[] logits = output.DownloadToArray();

            return logits;
        }
    }
}