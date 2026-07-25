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

        // ApplyObstacleOverlay()/ClearObstacles() used to exist here, wired to a demo
        // "Obstacles" toggle. Off wiped the real, terrain-derived ObsGrid (loaded from
        // real slope/turbulence data) to all-zero; On replaced it with a handful of
        // random synthetic hazards and forced blocking regardless of altitude, which
        // env/rwanda_env.py's step() never does -- it blocks unconditionally on real
        // hazards whenever altitude < 0.5, with no toggle at all. So the demo control
        // was never showing the real system either way: Off showed an empty field
        // that doesn't exist during training, On showed a fake field with fake rules.
        // Real per-cell hazards, loaded once from the zone JSON, are simply never
        // touched now -- see ActionDispatcher.Step()'s obstacle-blocking logic.
    }
}
