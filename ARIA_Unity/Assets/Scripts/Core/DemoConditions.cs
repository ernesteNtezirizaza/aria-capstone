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
                    return ARIAConstants.RAINFALL_SUNNY_THRESH - 0.08f;
            }
        }

        public static void ApplyObstacleOverlay(ZoneData zone, int seed)
        {
        
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
