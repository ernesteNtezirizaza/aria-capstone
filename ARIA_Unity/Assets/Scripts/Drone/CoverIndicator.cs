using UnityEngine;
using ARIA.Drone;

namespace ARIA.Drone
{
    public class CoverIndicator : MonoBehaviour
    {
        [Tooltip("Assign the same DroneController driving the episode.")]
        public DroneController drone;

        private GameObject _shell;
        private GameObject _rim;

        public void Bind(DroneController d)
        {
            if (drone != null) drone.OnStepTaken -= HandleStep;
            drone = d;
            if (drone != null) drone.OnStepTaken += HandleStep;
            BuildShell();
        }

        void OnDisable()
        {
            if (drone != null) drone.OnStepTaken -= HandleStep;
        }

        private void BuildShell()
        {
            if (_shell != null) return;

            Transform parent = transform;

            _shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _shell.name = "CoverShell";
            Destroy(_shell.GetComponent<Collider>());
            _shell.transform.SetParent(parent, false);
            
            _shell.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            _shell.transform.localScale = new Vector3(7f, 5f, 7f);

            var mat = MaterialHelper.GetDefaultMaterial();
            mat.color = new Color(0.2f, 0.85f, 1f, 0.45f); // translucent bright cyan -- seed drop stays visible through it
            mat.SetFloat("_Mode", 3); // Transparent
            mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetInt("_ZWrite", 0);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            mat.renderQueue = 3000;
            mat.EnableKeyword("_EMISSION");
            mat.SetColor("_EmissionColor", new Color(0.3f, 1.5f, 1.8f)); // strong glow so it still reads clearly despite the transparency
            _shell.GetComponent<Renderer>().material = mat;

            _rim = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _rim.name = "CoverRim";
            Destroy(_rim.GetComponent<Collider>());
            _rim.transform.SetParent(parent, false);
            _rim.transform.localPosition = new Vector3(0f, -0.3f, 0f);
            _rim.transform.localScale = new Vector3(7.3f, 0.08f, 7.3f);
            var rimMat = MaterialHelper.GetDefaultMaterial();
            rimMat.color = new Color(0.6f, 1f, 1f, 1f);
            rimMat.EnableKeyword("_EMISSION");
            rimMat.SetColor("_EmissionColor", new Color(0.6f, 1.6f, 1.9f));
            _rim.GetComponent<Renderer>().material = rimMat;

            _shell.SetActive(false);
            _rim.SetActive(false);
        }

        private void HandleStep(DroneController d)
        {
            if (_shell == null || d.State == null) return;
            bool deployed = d.State.CoverDeployed;
            _shell.SetActive(deployed);
            if (_rim != null) _rim.SetActive(deployed);
        }
    }
}
