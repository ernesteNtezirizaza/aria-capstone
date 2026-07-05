using ARIA.Core;

namespace ARIA.Systems
{
    public class WeatherSystem
    {
        public int   CurrentSeason  { get; private set; }
        public int   WeatherState   { get; private set; } // WEATHER_SUNNY / WEATHER_RAINY
        public float SolarRate      { get; private set; }
        public float ExtraDrain     { get; private set; }

        public WeatherSystem()
        {
            Reset();
        }

        // weather_system.py __init__ / reset()
        public void Reset()
        {
            CurrentSeason = 0;
            WeatherState  = ARIAConstants.WEATHER_SUNNY;
            SolarRate     = 0f;
            ExtraDrain    = 0f;
        }

        /// <summary>
        /// Update weather state from current rainfall value.
        /// Mirrors weather_system.py step() exactly.
        /// </summary>
        /// <param name="rainfallValue">Normalised CHIRPS-equivalent rainfall [0,1] at drone's position.</param>
        /// <param name="timestep">Current episode timestep.</param>
        public void Step(float rainfallValue, int timestep)
        {
            // self.current_season = (timestep // _SEASON_LENGTH) % N_SEASONS
            CurrentSeason = (timestep / ARIAConstants.SEASON_LENGTH) % ARIAConstants.N_SEASONS;

            if (rainfallValue < ARIAConstants.RAINFALL_SUNNY_THRESH)
            {
                WeatherState = ARIAConstants.WEATHER_SUNNY;
                // solar_rate = SOLAR_CHARGE_RATE * (1 - rainfall/THRESH)
                SolarRate = ARIAConstants.SOLAR_CHARGE_RATE *
                    (1f - rainfallValue / ARIAConstants.RAINFALL_SUNNY_THRESH);
                ExtraDrain = 0f;
            }
            else
            {
                WeatherState = ARIAConstants.WEATHER_RAINY;
                SolarRate    = 0f;
                ExtraDrain   = ARIAConstants.BATTERY_DRAIN_RAIN - ARIAConstants.BATTERY_DRAIN_SUNNY;
            }
        }

        public bool IsRainy() => WeatherState == ARIAConstants.WEATHER_RAINY;
        public bool IsSunny() => WeatherState == ARIAConstants.WEATHER_SUNNY;
    }
}
