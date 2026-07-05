using UnityEngine;
using ARIA.Drone;

namespace ARIA.Drone
{
    [RequireComponent(typeof(ParticleSystem))]
    public class RainEffect : MonoBehaviour
    {
        [Tooltip("Assign the same DroneController driving the episode.")]
        public DroneController drone;

        [Tooltip("Rain follows this transform (typically the Main Camera) so it's " +
                 "always overhead wherever you're looking, rather than fixed at origin.")]
        public Transform followTarget;

        private ParticleSystem _ps;
        private bool _wasRainy;

        void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
            ConfigureParticles();
        }

        public void Bind(DroneController d)
        {
            if (drone != null) drone.OnStepTaken -= HandleStep;
            drone = d;
            if (drone != null) drone.OnStepTaken += HandleStep;
        }

        void OnDisable()
        {
            if (drone != null) drone.OnStepTaken -= HandleStep;
        }

        void LateUpdate()
        {
            if (followTarget != null)
            {
                transform.position = followTarget.position + Vector3.up * 15f;
            }
        }

        private void HandleStep(DroneController d)
        {
            bool isRainy = d.State != null && d.State.Weather.IsRainy();
            if (isRainy != _wasRainy)
            {
                if (isRainy) _ps.Play();
                else _ps.Stop();
                _wasRainy = isRainy;
            }
        }

        private void ConfigureParticles()
        {
            var main = _ps.main;
            main.loop = true;
            main.startLifetime = 1.2f;
            main.startSpeed = 18f;
            main.startSize = 0.08f;
            main.startColor = new Color(0.7f, 0.8f, 1f, 0.55f);
            main.maxParticles = 2000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            var emission = _ps.emission;
            emission.rateOverTime = 600f;

            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(40f, 1f, 40f);

            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;
            vel.y = new ParticleSystem.MinMaxCurve(-18f);

            var renderer = _ps.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.06f;
            renderer.lengthScale = 3f;
            var mat = new Material(Shader.Find("Standard"));
            mat.color = new Color(0.7f, 0.8f, 1f, 0.6f);
            renderer.material = mat;

            _ps.Stop(); // starts off; HandleStep turns it on when real weather is rainy
        }
    }
}
