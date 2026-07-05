using UnityEngine;
using ARIA.Core;

namespace ARIA.Core
{
    public enum WeatherMode
    {
        RealData,    
        AutoCycle,   
        ForceSunny,  
        ForceRainy,  
    }

    public static class DemoConditions
    {
        public static WeatherMode WeatherMode = WeatherMode.RealData;
        public static bool ObstacleOverlayEnabled = false;

        [Tooltip("Real simulation steps per full sunny<->rainy cycle, when WeatherMode.AutoCycle is active.")]
        public static int WeatherCyclePeriod = 40;

        public static float GetEffectiveRainfall(float realRainfall, int timestep)
        {
            switch (WeatherMode)
            {
                case WeatherMode.ForceSunny:
                    return ARIAConstants.RAINFALL_SUNNY_THRESH - 0.08f;

                case WeatherMode.ForceRainy:
                    return ARIAConstants.RAINFALL_SUNNY_THRESH + 0.08f;

                case WeatherMode.AutoCycle:
                {
                    float t = (timestep % WeatherCyclePeriod) / (float)WeatherCyclePeriod;
                    float triangle = 1f - Mathf.Abs(2f * t - 1f); // 0 -> 1 -> 0
                    float amplitude = 0.08f;
                    return ARIAConstants.RAINFALL_SUNNY_THRESH - amplitude + triangle * amplitude * 2f;
                }

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
