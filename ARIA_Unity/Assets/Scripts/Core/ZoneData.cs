using UnityEngine;
using ARIA.Core;

namespace ARIA.Core
{
    public class ZoneData
    {
        public readonly int Size;

        // terrain[y, x, channel] -- channel layout matches
        // self.terrain in rwanda_env.py:
        //   0 = elevation, 1 = slope, 2 = soil, 3 = rainfall, 4 = landcover
        public float[,,] Terrain;       // [Size, Size, N_CHANNELS]
        public float[,]  DistGrid;      // distance-to-protected-area proximity [0,1]
        public float[,]  ObsGrid;       // obstacle map [0,1]
        public bool[,]   NoPlant;       // no-plant mask

        public ZoneData(int size)
        {
            Size = size;
            Terrain  = new float[size, size, ARIAConstants.N_CHANNELS];
            DistGrid = new float[size, size];
            ObsGrid  = new float[size, size];
            NoPlant  = new bool[size, size];
        }

        public float SoilAt(int y, int x)  => Terrain[y, x, 2];
        public float SlopeAt(int y, int x) => Terrain[y, x, 1];

        // Composite zone-level suitability: soil + rain - slope, weighted
        // the same way as configs.config.ZONE_SUITABILITY_WEIGHTS on the
        // Python training side. Previously this only averaged the soil
        // channel (Terrain[y,x,2]), so the abort decision and the
        // mission_vector's zone_score ignored rain and slope entirely even
        // though the trained policy was taught (via the Python reward
        // function) that all three matter.
        public float ZoneSuitability()
        {
            float soilSum = 0f, rainSum = 0f, slopeSum = 0f;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    soilSum  += Terrain[y, x, 2];
                    rainSum  += Terrain[y, x, 3];
                    slopeSum += Terrain[y, x, 1]; // already 0-1 normalised, same as Python's slope_norm channel
                }
            }
            int n = Size * Size;
            float soil = soilSum / n, rain = rainSum / n, slopePen = slopeSum / n;

            float wSoil = ARIAConstants.ZONE_SUIT_W_SOIL;
            float wRain = ARIAConstants.ZONE_SUIT_W_RAIN;
            float wSlope = ARIAConstants.ZONE_SUIT_W_SLOPE;

            float score = (wSoil * soil + wRain * rain - wSlope * slopePen) / (wSoil + wRain + wSlope);
            return Mathf.Clamp01(score);
        }

        public float NoPlantFraction()
        {
            int count = 0;
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                    if (NoPlant[y, x]) count++;
            return (float)count / (Size * Size);
        }
    }

    public static class ZoneGenerator
    {
        public static ZoneData GenerateZone(int seed, int size = ARIAConstants.ZONE_SIZE)
        {
            var rng = new System.Random(seed);
            float offsetX = (float)rng.NextDouble() * 1000f;
            float offsetY = (float)rng.NextDouble() * 1000f;

            var zone = new ZoneData(size);

            // Base elevation noise (channel 0)
            float[,] elevation = new float[size, size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float nx = (x + offsetX) * 0.03f;
                    float ny = (y + offsetY) * 0.03f;
                    elevation[y, x] = Mathf.PerlinNoise(nx, ny);
                }
            }

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    // Channel 0: elevation
                    zone.Terrain[y, x, 0] = elevation[y, x];

                    // Channel 1: slope (derived from local elevation gradient)
                    float dx = (x < size - 1) ? elevation[y, Mathf.Min(x + 1, size - 1)] - elevation[y, x] : 0f;
                    float dy = (y < size - 1) ? elevation[Mathf.Min(y + 1, size - 1), x] - elevation[y, x] : 0f;
                    float slope = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) * 6f);
                    zone.Terrain[y, x, 1] = slope;

                    // Channel 2: soil quality
                    float soilNoise = Mathf.PerlinNoise(
                        (x + offsetX + 500f) * 0.05f,
                        (y + offsetY + 500f) * 0.05f);
                    zone.Terrain[y, x, 2] = Mathf.Clamp01(soilNoise * 0.7f + 0.25f);

                    // Channel 3: rainfall -- separate noise layer
                    float rainNoise = Mathf.PerlinNoise(
                        (x + offsetX + 1000f) * 0.04f,
                        (y + offsetY + 1000f) * 0.04f);
                    zone.Terrain[y, x, 3] = Mathf.Clamp01(rainNoise * 0.6f + 0.2f);

                    // Channel 4: landcover -- coarse noise (forest/open/etc proxy)
                    float lcNoise = Mathf.PerlinNoise(
                        (x + offsetX + 1500f) * 0.02f,
                        (y + offsetY + 1500f) * 0.02f);
                    zone.Terrain[y, x, 4] = lcNoise;

                    // Obstacle map: high where slope is steep (terrain rises)
                    float canopyNoise = Mathf.PerlinNoise(
                        (x + offsetX + 2000f) * 0.06f,
                        (y + offsetY + 2000f) * 0.06f);
                    zone.ObsGrid[y, x] = Mathf.Clamp01(slope * 0.6f + canopyNoise * 0.4f);

                    // No-plant mask: true on the steepest terrain
                    zone.NoPlant[y, x] = slope > 0.75f;

                    // Distance/proximity to a synthetic "protected area"
                    // placed at a fixed offset from zone centre.
                    float px = size * 0.7f, py = size * 0.3f;
                    float distToProtected = Mathf.Sqrt(
                        (x - px) * (x - px) + (y - py) * (y - py));
                    zone.DistGrid[y, x] = Mathf.Clamp01(
                        1f - distToProtected / (size * 0.5f));
                }
            }

            return zone;
        }
    }
}
