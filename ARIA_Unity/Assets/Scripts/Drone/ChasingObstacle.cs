using UnityEngine;
using ARIA.Core;

namespace ARIA.Drone
{
    /// A dynamic, demo-only hazard, separate from AerialObstacleVisualizer's
    /// static real-hazard markers: when DemoConditions.ShowHazardMarkers is
    /// on, a helicopter actively closes in on the drone's current position,
    /// and the drone visually dodges away from it the closer it gets.
    ///
    /// Deliberately layered entirely on top of the real simulation rather
    /// than wired into it: the dodge only nudges the drone's RENDERED
    /// transform (in LateUpdate, after DroneController's own Update has
    /// already placed it for this frame), never State.X/State.Y or
    /// anything ActionDispatcher.Step() reads. So seeding, navigation, and
    /// every other step of the actual mission continue completely
    /// unaffected underneath the chase -- this is spectacle, not a change
    /// to what the trained policy is actually doing.
    public class ChasingObstacle : MonoBehaviour
    {
        [Tooltip("Assign the same DroneController driving the episode.")]
        public DroneController drone;

        [Tooltip("Resources/<name>.prefab to spawn as the chaser -- same helicopter model used for the static hazard markers.")]
        public string helicopterResourceName = "HelicopterHazard";

        [Tooltip("World units/second the chaser closes the distance to the drone.")]
        public float chaseSpeed = 7f;

        [Tooltip("The chaser holds off at this distance rather than flying into the drone -- reads as circling/stalking.")]
        public float standoffDistance = 9f;

        [Tooltip("Once the chaser is within this distance, the drone starts visually dodging away from it.")]
        public float dodgeTriggerDistance = 16f;

        [Tooltip("How strongly (world units/second, at maximum urgency) the drone's rendered position nudges away from the chaser.")]
        public float dodgeStrength = 8f;

        [Tooltip("Visual scale of the chaser -- bigger than the static hazard markers so an actively-approaching threat reads as unmistakable.")]
        public float chaserScale = 3.5f;

        private GameObject _chaser;
        private bool _active;

        public void Bind(DroneController d) => drone = d;

        void Update()
        {
            bool wantActive = DemoConditions.ShowHazardMarkers;
            if (wantActive && !_active) Spawn();
            else if (!wantActive && _active) Despawn();

            if (!_active || _chaser == null || drone == null) return;

            Vector3 toDrone = drone.transform.position - _chaser.transform.position;
            float dist = toDrone.magnitude;
            if (dist > standoffDistance)
            {
                Vector3 dir = toDrone.normalized;
                _chaser.transform.position += dir * chaseSpeed * Time.deltaTime;
            }
            if (toDrone.sqrMagnitude > 0.0001f)
                _chaser.transform.rotation = Quaternion.LookRotation(toDrone.normalized, Vector3.up);
        }

        void LateUpdate()
        {
            if (!_active || _chaser == null || drone == null) return;

            Vector3 away = drone.transform.position - _chaser.transform.position;
            float dist = away.magnitude;
            if (dist < dodgeTriggerDistance && dist > 0.01f)
            {
                float urgency = 1f - Mathf.Clamp01(dist / dodgeTriggerDistance);
                Vector3 dodgeDir = away.normalized;
                drone.transform.position += dodgeDir * dodgeStrength * urgency * Time.deltaTime;
            }
        }

        private void Spawn()
        {
            var prefab = Resources.Load<GameObject>(helicopterResourceName);
            if (prefab == null)
            {
                Debug.LogWarning($"[ChasingObstacle] No prefab found at Resources/{helicopterResourceName} -- chase feature disabled.");
                return;
            }

            _chaser = Instantiate(prefab);
            _chaser.name = "ChasingObstacle";
            _chaser.transform.localScale = Vector3.one * chaserScale;

            foreach (var col in _chaser.GetComponentsInChildren<Collider>())
                Destroy(col);

            var anim = _chaser.GetComponentInChildren<Animation>();
            if (anim != null && anim.clip != null)
            {
                anim.wrapMode = WrapMode.Loop;
                anim.Play();
            }
            else
            {
                var animator = _chaser.GetComponentInChildren<Animator>();
                if (animator != null) animator.Play(0, 0, 0f);
            }

            // Start well clear of the drone so the approach is visible, not instant.
            Vector3 startOffset = new Vector3(Random.Range(-1f, 1f), 0.3f, Random.Range(-1f, 1f)).normalized * (dodgeTriggerDistance * 2f);
            _chaser.transform.position = drone.transform.position + startOffset + Vector3.up * 4f;

            _active = true;
        }

        private void Despawn()
        {
            if (_chaser != null) Destroy(_chaser);
            _chaser = null;
            _active = false;
        }
    }
}
