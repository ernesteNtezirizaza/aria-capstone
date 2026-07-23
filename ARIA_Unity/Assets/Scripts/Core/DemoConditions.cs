using UnityEngine;
using ARIA.Core;

namespace ARIA.Core
{
    public enum WeatherMode
    {
        RealData,
        ForceSunny,
        ForceRainy,
    }

    public static class DemoConditions
    {
        public static WeatherMode WeatherMode = WeatherMode.RealData;
        public static bool ObstacleOverlayEnabled = false;
        public static bool AnimalDisturbanceEnabled = false;

        public static float GetEffectiveRainfall(float realRainfall, int timestep)
        {
            switch (WeatherMode)
            {
                case WeatherMode.ForceSunny:
                    return ARIAConstants.RAINFALL_SUNNY_THRESH - 0.08f;

                case WeatherMode.ForceRainy:
                    return ARIAConstants.RAINFALL_SUNNY_THRESH + 0.08f;

                case WeatherMode.RealData:
                default:
                    // BUG FIX: this used to return the same hardcoded value
                    // as ForceSunny, completely ignoring realRainfall --
                    // "RealData" mode was indistinguishable from
                    // "ForceSunny" regardless of what actually happened.
                    // Confirmed as a real, structural cause of weather (and
                    // therefore battery drain/solar balance) never varying
                    // with genuine conditions, consistent with battery
                    // reading 100% across every demo screenshot.
                    return realRainfall;
            }
        }

        public static void ApplyObstacleOverlay(ZoneData zone, int seed)
        {
            // Previously a complete no-op -- confirmed by direct inspection,
            // toggling this on in the demo changed nothing about the real
            // obstacle grid the policy actually observes. Seeds a modest,
            // reproducible-per-zone set of obstacles (same convention as
            // real, static obstacle clusters (same convention as
            // AerialObstacleVisualizer's terrain-fixed markers: 0.95f, safely above
            // OBSTACLE_THRESHOLD) onto real plantable ground, so enabling
            // this toggle has a genuine, visible effect tied to the same
            // grid the policy's obstacle_map observation reads from.
            if (zone == null) return;
            var rng = new System.Random(seed);
            int size = zone.Size;
            int count = Mathf.Max(1, size / 12); // scales modestly with zone size

            int placed = 0;
            int attempts = 0;
            int maxAttempts = count * 20;
            while (placed < count && attempts < maxAttempts)
            {
                attempts++;
                int x = rng.Next(0, size);
                int y = rng.Next(0, size);
                if (zone.NoPlant[y, x]) continue;
                zone.ObsGrid[y, x] = 0.95f;
                placed++;
            }
        }
        
        public static void ClearObstacles(ZoneData zone)
        {
            if (zone == null) return;
            int size = zone.Size;
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    zone.ObsGrid[y, x] = 0f;
        }
    }
}
