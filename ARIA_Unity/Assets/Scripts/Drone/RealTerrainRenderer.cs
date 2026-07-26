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
            // Bilinear, not Point: with Point filtering each cell reads as a
            // hard-edged pixel (a data grid); bilinear blends between
            // neighbouring cells' colours, which combined with the
            // natural-earth-tone palette below is what makes this read as
            // ground texture rather than a suitability heatmap.
            _texture.filterMode = FilterMode.Bilinear;
            _texture.wrapMode = TextureWrapMode.Clamp;

            // Deliberately its own baked Unlit material asset
            // (Assets/Resources/TerrainUnlitMaterial.mat, built by
            // UnlitTerrainMaterialBuilder), not Shader.Find("Unlit/Texture")
            // at runtime and not the shared MaterialHelper material every
            // other object uses. Runtime Shader.Find works fine in the
            // Editor (which always has every built-in shader available),
            // but nothing else in the project references "Unlit/Texture"
            // from a serialized asset, so the WebGL build pipeline can
            // strip it as unused -- meaning Shader.Find would silently
            // return null in the actual deployed build and fall back
            // invisibly, which is suspected to be exactly why four
            // consecutive lighting/material fixes (light+ambient, fog
            // distance, reverting a shader change, disabling fog outright)
            // never changed anything: the terrain was likely never actually
            // using Unlit at all. A real asset reference guarantees the
            // shader survives stripping, same fix already applied to
            // DummyStandardMaterial earlier this session.
            var mat = Resources.Load<Material>("TerrainUnlitMaterial");
            if (mat == null)
            {
                Debug.LogError("[RealTerrainRenderer] TerrainUnlitMaterial not found in Resources -- " +
                    "falling back to the shared lit material. Run UnlitTerrainMaterialBuilder in the Editor.");
                mat = MaterialHelper.GetDefaultMaterial();
            }
            else
            {
                mat = new Material(mat);
            }
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
                _texture.filterMode = FilterMode.Bilinear;
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
            // TEMP DIAGNOSTIC v2: even a dedicated Unlit material -- which
            // mathematically cannot be affected by light, ambient, or
            // shader choice -- still rendered washed-out pale yellow. That
            // rules out the entire lighting pipeline, which was the target
            // of all three previous fix attempts. This can now only mean
            // either the colour DATA itself isn't what SampleCellColour is
            // supposed to produce, or this code isn't actually the version
            // running live. Logging both the raw zone data and the actual
            // computed pixel values, plus a version canary, to find out
            // which, instead of guessing a fifth time.
            {
                Color minC = pixels[0], maxC = pixels[0];
                double sumR = 0, sumG = 0, sumB = 0;
                foreach (var p in pixels)
                {
                    sumR += p.r; sumG += p.g; sumB += p.b;
                    if (p.r + p.g + p.b < minC.r + minC.g + minC.b) minC = p;
                    if (p.r + p.g + p.b > maxC.r + maxC.g + maxC.b) maxC = p;
                }
                int n = pixels.Length;
                int sx = size / 2, sy = size / 2;
                Debug.Log($"[TerrainDiag-v2] CANARY -- this build's RealTerrainRenderer IS running. " +
                    $"zoneSize={size} avgPixel=({sumR / n:F3},{sumG / n:F3},{sumB / n:F3}) " +
                    $"minPixel=({minC.r:F3},{minC.g:F3},{minC.b:F3}) maxPixel=({maxC.r:F3},{maxC.g:F3},{maxC.b:F3}) " +
                    $"centre({sx},{sy}): soil={zone.Terrain[sy, sx, 2]:F3} rain={zone.Terrain[sy, sx, 3]:F3} " +
                    $"slope={zone.Terrain[sy, sx, 1]:F3} elev={zone.Terrain[sy, sx, 0]:F3} " +
                    $"noPlant={zone.NoPlant[sy, sx]} computedPixel={pixels[sy * size + sx]}");
            }
            _texture.SetPixels(pixels);
            _texture.Apply(false);

            BuildHeightmapMesh(size, _heightMap);
        }

        // Natural earth-tone ground colour driven by the same real soil/
        // rain/slope data the policy reasons over, rather than the raw
        // suitability score painted directly as a blue-to-orange gradient.
        // A suitability heatmap reads as a data visualisation; this reads
        // as ground, which is what an audience needs to see to believe
        // it's a real landscape and not a chart.
        private static readonly Color DryEarth   = new Color(0.55f, 0.46f, 0.30f);
        private static readonly Color GrassGreen = new Color(0.27f, 0.44f, 0.15f);
        private static readonly Color Rock       = new Color(0.43f, 0.40f, 0.36f);

        private Color SampleCellColour(ZoneData zone, int x, int y)
        {
            float soil  = zone.Terrain[y, x, 2];
            float rain  = zone.Terrain[y, x, 3];
            float slope = zone.Terrain[y, x, 1];

            // How lush this cell would actually look: good soil+rain reads
            // green, poor reads as dry/dusty earth. Steep ground holds less
            // topsoil in reality, so slope pulls it toward bare rock.
            float lushness = Mathf.Clamp01((soil * 0.5f + rain * 0.5f) * (1f - slope * 0.6f));
            Color ground = Color.Lerp(DryEarth, GrassGreen, lushness);
            Color baseColour = zone.NoPlant[y, x]
                ? Rock
                : Color.Lerp(ground, Rock, slope * 0.5f);

            // Small per-cell mottling so the ground doesn't read as a flat
            // gradient fill -- cheap stand-in for a real ground texture.
            float n = (Mathf.PerlinNoise(x * 0.15f, y * 0.15f) - 0.5f) * 0.12f;
            return new Color(
                Mathf.Clamp01(baseColour.r + n),
                Mathf.Clamp01(baseColour.g + n),
                Mathf.Clamp01(baseColour.b + n));
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

