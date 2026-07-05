"""
env/weather_system.py
=====================
Derives weather state from CHIRPS rainfall values.
"""

import numpy as np
from configs.config import (
    WEATHER_SUNNY, WEATHER_RAINY,
    RAINFALL_SUNNY_THRESH, N_SEASONS,
    SOLAR_CHARGE_RATE, BATTERY_DRAIN_SUNNY, BATTERY_DRAIN_RAIN,
    MAX_STEPS
)

# Season length derived from episode length and number of seasons
_SEASON_LENGTH = MAX_STEPS // N_SEASONS


class WeatherSystem:
    """
    Manages weather state derived from CHIRPS seasonal rainfall.

    The current rainfall value at the drone's position determines:
      - Weather state (SUNNY or RAINY)
      - Solar generation rate
      - Battery drain rate
      - Whether rain cover should be deployed
    """

    def __init__(self):
        self.current_season  = 0
        self.weather_state   = WEATHER_SUNNY
        self.solar_rate      = 0.0
        self.extra_drain     = 0.0

    def reset(self):
        self.current_season = 0
        self.weather_state  = WEATHER_SUNNY
        self.solar_rate     = 0.0
        self.extra_drain    = 0.0

    def step(self, rainfall_value: float, timestep: int):
        """
        Update weather state from current rainfall value.

        Parameters
        ----------
        rainfall_value : float [0,1]
            Normalised CHIRPS rainfall at drone's current position.
        timestep : int
            Current episode timestep — advances season every 80 steps.
        """
        # Advance season based on episode length and number of seasons
        self.current_season = (timestep // _SEASON_LENGTH) % N_SEASONS

        if rainfall_value < RAINFALL_SUNNY_THRESH:
            self.weather_state = WEATHER_SUNNY
            self.solar_rate    = SOLAR_CHARGE_RATE * (
                1.0 - rainfall_value / RAINFALL_SUNNY_THRESH
            )
            self.extra_drain   = 0.0
        else:
            self.weather_state = WEATHER_RAINY
            self.solar_rate    = 0.0
            self.extra_drain   = BATTERY_DRAIN_RAIN - BATTERY_DRAIN_SUNNY

    def is_rainy(self) -> bool:
        return self.weather_state == WEATHER_RAINY

    def is_sunny(self) -> bool:
        return self.weather_state == WEATHER_SUNNY

    def get_state_vector(self) -> np.ndarray:
        """Returns [weather_norm, solar_rate, extra_drain] as float32."""
        return np.array([
            float(self.weather_state),
            self.solar_rate / SOLAR_CHARGE_RATE,                          # normalised
            self.extra_drain / (BATTERY_DRAIN_RAIN - BATTERY_DRAIN_SUNNY + 1e-8),
        ], dtype=np.float32)
