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

        public EnergyStepResult Step(WeatherSystem weather)
        {
            bool sunny = weather.IsSunny();

            if (sunny)
            {
                SolarInput    = ARIAConstants.SOLAR_CHARGE_RATE;
                DrainThisStep = 0f;
                Battery = Mathf.Clamp(Battery + SolarInput, 0f, ARIAConstants.BATTERY_MAX);
            }
            else
            {
                SolarInput    = 0f;
                DrainThisStep = ARIAConstants.BATTERY_DRAIN_RAIN;
                Battery = Mathf.Clamp(Battery - DrainThisStep, 0f, ARIAConstants.BATTERY_MAX);
            }

            TotalSolar += SolarInput;
            TotalDrain += DrainThisStep;

            if (Battery <= ARIAConstants.BATTERY_CRITICAL)
                EmptyEvents++;

            return new EnergyStepResult
            {
                Battery      = Battery,
                SolarInput   = SolarInput,
                Drain        = DrainThisStep,
                ShouldReturn = Battery < ARIAConstants.BATTERY_RETURN_THRESH,
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
