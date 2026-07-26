using UnityEngine;
using ARIA.Core;

namespace ARIA.Systems
{
    public struct EnergyStepResult
    {
        public float Battery;
        public float SolarInput;
        public float Drain;
        public bool  ShouldReturn;
        public bool  IsCritical;
    }

    public class EnergySystem
    {
        public float Battery        { get; private set; }
        public float SolarInput     { get; private set; }
        public float DrainThisStep  { get; private set; }
        public float TotalSolar     { get; private set; }
        public float TotalDrain     { get; private set; }
        public int   EmptyEvents    { get; private set; }

        public EnergySystem()
        {
            Reset();
        }

        // energy_system.py __init__ / reset()
        public void Reset()
        {
            Battery       = ARIAConstants.BATTERY_INIT;
            SolarInput    = 0f;
            DrainThisStep = 0f;
            TotalSolar    = 0f;
            TotalDrain    = 0f;
            EmptyEvents   = 0;
        }

        /// <summary>
        /// Mirrors energy_system.py's step() exactly. stepsToBase is the
        /// Chebyshev distance from the drone's current position to base
        /// (matches the diagonal scripted return movement: dx/dy both
        /// close simultaneously each step, so steps needed =
        /// max(|dx|,|dy|), not Manhattan distance) -- used to make
        /// ShouldReturn distance-aware. A fixed threshold was a real,
        /// documented bug in Python (see the matching comment there): the
        /// margin between "should return" and "critical" only covers
        /// 12-25 steps of flight, but the drone can range far further
        /// than that from base, so a drone already beyond the margin
        /// when a fixed threshold fired couldn't physically survive the
        /// trip back.
        /// </summary>
        public EnergyStepResult Step(WeatherSystem weather, int stepsToBase = 0)
        {
            bool sunny = weather.IsSunny();

            if (sunny)
            {
                // Deliberate product decision, overriding the rainfall-
                // proportional solar formula rwanda_env.py trained under:
                // sunny weather fully sustains the battery at 100% rather
                // than merely reducing drain by some fraction. Simpler,
                // more intuitive demo behaviour -- sunny means safe, rainy
                // means the battery actually drains.
                SolarInput    = ARIAConstants.BATTERY_MAX;
                DrainThisStep = 0f;
                Battery       = ARIAConstants.BATTERY_MAX;
            }
            else
            {
                SolarInput    = 0f;
                DrainThisStep = ARIAConstants.BATTERY_DRAIN_RAIN;
                Battery = Mathf.Clamp(Battery + SolarInput - DrainThisStep, 0f, ARIAConstants.BATTERY_MAX);
            }

            TotalSolar += SolarInput;
            TotalDrain += DrainThisStep;

            if (Battery <= ARIAConstants.BATTERY_CRITICAL)
                EmptyEvents++;

            float safeMargin  = stepsToBase * ARIAConstants.BATTERY_DRAIN_RAIN + ARIAConstants.BATTERY_CRITICAL;
            float returnThresh = Mathf.Max(ARIAConstants.BATTERY_RETURN_THRESH, safeMargin);

            return new EnergyStepResult
            {
                Battery      = Battery,
                SolarInput   = SolarInput,
                Drain        = DrainThisStep,
                ShouldReturn = Battery < returnThresh,
                IsCritical   = Battery <= ARIAConstants.BATTERY_CRITICAL,
            };
        }

        /// <summary>Called when drone lands at base to recharge.</summary>
        public void Recharge(float amount = 1.0f)
        {
            Battery = Mathf.Min(ARIAConstants.BATTERY_INIT, Battery + amount);
        }

        /// <summary>Normalised battery level [0,1] -- matches get_state().</summary>
        public float GetState() => Battery / ARIAConstants.BATTERY_MAX;
    }
}
