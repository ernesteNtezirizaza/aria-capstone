using UnityEngine;

namespace ARIA.Drone
{
    public class TerrainOrbitCamera : MonoBehaviour
    {
        [Tooltip("World point the camera stays fixed on when followTarget is null, " +
                 "and the resting point followTarget eases away from when it isn't.")]
        public Vector3 target = Vector3.zero;

        [Tooltip("Optional -- when assigned, the camera's actual look point eases " +
                 "toward this transform's position (heavily damped) instead of staying " +
                 "rigidly on `target`.")]
        public Transform followTarget;

        [Tooltip("Seconds for the follow to catch up -- larger is smoother/slower.")]
        public float followSmoothTime = 1.4f;

        public float dist = 80f;
        public float pitch = 30f;
        public float yaw = 0f;

        public float minDist = 20f;
        public float maxDist = 400f;
        public float minPitch = 8f;
        public float maxPitch = 80f;

        private Vector3 _currentLookPoint;
        private Vector3 _followVelocity;
        private bool _initialised;

        void LateUpdate()
        {
            if (!_initialised)
            {
                _currentLookPoint = target;
                _initialised = true;
            }

            if (Input.GetMouseButton(1))
            {
                yaw += Input.GetAxis("Mouse X") * 3f;
                pitch -= Input.GetAxis("Mouse Y") * 3f;
                pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
            }
            dist -= Input.GetAxis("Mouse ScrollWheel") * 30f;
            dist = Mathf.Clamp(dist, minDist, maxDist);

            Vector3 desiredLookPoint = target;
            if (followTarget != null)
            {
                desiredLookPoint = new Vector3(followTarget.position.x, target.y, followTarget.position.z);
            }

            _currentLookPoint = Vector3.SmoothDamp(_currentLookPoint, desiredLookPoint, ref _followVelocity, followSmoothTime);

            Quaternion rot = Quaternion.Euler(pitch, yaw, 0);
            transform.position = _currentLookPoint + rot * new Vector3(0, 0, -dist);
            transform.LookAt(_currentLookPoint);
        }
    }
}
