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

        // Drives both AerialObstacleVisualizer's static real-hazard markers
        // and ChasingObstacle's dynamic hazard (see that file) -- off by
        // default per explicit request, so the demo opens clean and the
        // more dramatic chase behaviour is something you switch on
        // deliberately. Never touches ObsGrid or ActionDispatcher.Step()'s
        // blocking logic either way -- the real hazard grid the policy
        // reasons over is always active regardless of this flag.
        public static bool ShowHazardMarkers = false;

        public static float GetEffectiveRainfall(float realRainfall, int timestep)
        {
            switch (WeatherMode)
            {
                case WeatherMode.ForceSunny:
                    // Deliberately near-zero, not just "under the sunny
                    // threshold": SOLAR_CHARGE_RATE only exactly offsets
                    // BATTERY_DRAIN_SUNNY at rainfall == 0 (see
                    // ARIAConstants), so a value still close to
                    // RAINFALL_SUNNY_THRESH (as this used to be, THRESH -
                    // 0.08 = 0.186) leaves solar income well below drain and
                    // the battery keeps visibly falling even while this
                    // button reads "Force Sunny" -- confirmed live during
                    // testing (53% -> 39% over 15s under "Force Sunny").
                    return 0f;

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
