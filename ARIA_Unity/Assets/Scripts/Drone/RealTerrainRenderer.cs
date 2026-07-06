using UnityEngine;
using ARIA.Core;

namespace ARIA.Drone
{
    [RequireComponent(typeof(MeshRenderer))]
    public class RealTerrainRenderer : MonoBehaviour
    {
        [Tooltip("Assign the same DroneController whose State.Zone should be visualised.")]
        public DroneController drone;

        [Tooltip("World-space size of one terrain cell -- MUST match DroneController.cellSize.")]
        public float cellSize = 1.0f;

        private Texture2D _texture;
        private MeshRenderer _renderer;
        private GameObject _groundPlane;
        private ZoneData _zone;

        void OnEnable()
        {
            if (drone != null)
            {
                drone.OnEpisodeStarted += HandleNewEpisode;
            }
        }

        void OnDisable()
        {
            if (drone != null)
            {
                drone.OnEpisodeStarted -= HandleNewEpisode;
            }
        }

        void Start()
        {
            BuildGroundPlane();
            if (drone != null && drone.State != null)
            {
                Build(drone.State.Zone);
            }
        }

        private void HandleNewEpisode(DroneController d)
        {
            Build(d.State.Zone);
        }

        public void Bind(DroneController d)
        {
            if (drone != null) drone.OnEpisodeStarted -= HandleNewEpisode;
            drone = d;
            if (drone != null) drone.OnEpisodeStarted += HandleNewEpisode;

            if (drone != null && drone.State != null && _texture != null)
            {
                Build(drone.State.Zone);
            }
        }

        private void BuildGroundPlane()
        {
            int size = ARIAConstants.ZONE_SIZE;

            _groundPlane = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _groundPlane.name = "ZoneGroundPlane";
            _groundPlane.transform.SetParent(transform, false);

            float worldSize = size * cellSize;
            _groundPlane.transform.localScale = new Vector3(worldSize / 10f, 1f, worldSize / 10f);
            _groundPlane.transform.localPosition = new Vector3(
                worldSize / 2f - cellSize / 2f, 0f, worldSize / 2f - cellSize / 2f);

            _renderer = _groundPlane.GetComponent<MeshRenderer>();

            _texture = new Texture2D(size, size, TextureFormat.RGB24, false);
            _texture.filterMode = FilterMode.Point;
            _texture.wrapMode = TextureWrapMode.Clamp;

            var mat = MaterialHelper.GetDefaultMaterial();
            if (mat != null)
            {
                mat.mainTexture = _texture;
                _renderer.material = mat;
            }
        }

        public void Build(ZoneData zone)
        {
            _zone = zone;
            int size = zone.Size;

            if (_texture == null || _texture.width != size)
            {
                _texture = new Texture2D(size, size, TextureFormat.RGB24, false);
                _texture.filterMode = FilterMode.Point;
                _texture.wrapMode = TextureWrapMode.Clamp;
                if (_renderer != null) _renderer.material.mainTexture = _texture;
            }

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = SampleCellColour(zone, x, y);
                }
            }
            _texture.SetPixels(pixels);
            _texture.Apply(false);
        }

        private Color SampleCellColour(ZoneData zone, int x, int y)
        {
            float soil  = zone.Terrain[y, x, 2];
            float rain  = zone.Terrain[y, x, 3];
            float slope = zone.Terrain[y, x, 1]; // real, currently zero -- see file header
            float suit  = (soil * 0.4f + rain * 0.4f) * (1f - slope * 0.5f);

            Color baseColour;
            if (zone.NoPlant[y, x])
                baseColour = new Color(0.25f, 0.25f, 0.25f); // dark grey: no-plant cell
            else if (suit > 0.5f)      baseColour = new Color(suit, 1f - suit, 0f);
            else if (suit > 0.25f)     baseColour = new Color(suit * 0.5f, 1f - suit * 0.5f, 0f);
            else                       baseColour = new Color(0f, suit, 1f - suit);

            float obstacle = zone.ObsGrid[y, x];
            if (obstacle > ARIAConstants.OBSTACLE_THRESHOLD)
            {
                return Color.Lerp(baseColour, new Color(0.9f, 0.25f, 0.1f), 0.75f);
            }
            return baseColour;
        }

        public float GetHeight(int r, int c)
        {
            return 0f;
        }
    }
}
