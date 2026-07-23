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

        [Tooltip("World-space metres of vertical displacement for a cell at normalised elevation 1.0. " +
                 "Real Rwanda elevation ranges roughly 900-4500m; this is a visual exaggeration factor, " +
                 "not a literal scale, since a true 1:1 scale would be imperceptible next to a ~120-cell-wide zone.")]
        public float heightScale = 25.0f;

        private Texture2D _texture;
        private MeshRenderer _renderer;
        private MeshFilter _meshFilter;
        private GameObject _groundPlane;
        private ZoneData _zone;
        private float[,] _heightMap;

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

            _groundPlane = new GameObject("ZoneGroundPlane");
            _groundPlane.transform.SetParent(transform, false);
            _meshFilter = _groundPlane.AddComponent<MeshFilter>();
            _renderer = _groundPlane.AddComponent<MeshRenderer>();
            _groundPlane.AddComponent<MeshCollider>();

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

        /// Builds a size x size grid of quads (2 triangles each), one vertex
        /// per terrain cell corner, displaced in Y by real elevation. A
        /// 120x120 zone needs a (121 x 121) vertex grid -- 14,641 vertices,
        /// comfortably under Unity's 65,535-vertex 16-bit mesh limit, so no
        /// sub-mesh splitting is required at this zone size.
        private void BuildHeightmapMesh(int size, float[,] heightMap)
        {
            int verts1D = size + 1;
            var vertices = new Vector3[verts1D * verts1D];
            var uvs = new Vector2[verts1D * verts1D];
            var triangles = new int[size * size * 6];

            for (int y = 0; y < verts1D; y++)
            {
                for (int x = 0; x < verts1D; x++)
                {
                    int vi = y * verts1D + x;
                    // Sample height from the nearest in-bounds cell (vertex
                    // grid is one larger than the cell grid on each edge).
                    int sx = Mathf.Min(x, size - 1);
                    int sy = Mathf.Min(y, size - 1);
                    float h = heightMap[sy, sx];
                    vertices[vi] = new Vector3(x * cellSize, h, y * cellSize);
                    uvs[vi] = new Vector2((float)x / size, (float)y / size);
                }
            }

            int ti = 0;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int v00 = y * verts1D + x;
                    int v10 = v00 + 1;
                    int v01 = v00 + verts1D;
                    int v11 = v01 + 1;

                    triangles[ti++] = v00; triangles[ti++] = v01; triangles[ti++] = v10;
                    triangles[ti++] = v10; triangles[ti++] = v01; triangles[ti++] = v11;
                }
            }

            var mesh = new Mesh();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            _meshFilter.mesh = mesh;
            var collider = _groundPlane.GetComponent<MeshCollider>();
            if (collider != null) collider.sharedMesh = mesh;
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

            _heightMap = new float[size, size];
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = SampleCellColour(zone, x, y);
                    // Channel 0 = normalised elevation [0,1] (see ZoneData.cs
                    // channel-layout comment) -- scaled to world-space metres
                    // by heightScale for visibility at zone scale.
                    _heightMap[y, x] = zone.Terrain[y, x, 0] * heightScale;
                }
            }
            _texture.SetPixels(pixels);
            _texture.Apply(false);

            BuildHeightmapMesh(size, _heightMap);
        }

        private Color SampleCellColour(ZoneData zone, int x, int y)
        {
            float soil  = zone.Terrain[y, x, 2];
            float rain  = zone.Terrain[y, x, 3];
            float slope = zone.Terrain[y, x, 1];
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

        /// Real per-cell height matching the displaced mesh, in the same
        /// world-space units used to build it -- was previously hardcoded
        /// to 0 regardless of input, which is why trees, seed markers, and
        /// disturbance visuals (all of which call this) always sat at
        /// ground-zero even where real elevation existed.
        public float GetHeight(int r, int c)
        {
            if (_heightMap == null) return 0f;
            int size = _heightMap.GetLength(0);
            int rr = Mathf.Clamp(r, 0, size - 1);
            int cc = Mathf.Clamp(c, 0, size - 1);
            return _heightMap[rr, cc];
        }
    }
}

