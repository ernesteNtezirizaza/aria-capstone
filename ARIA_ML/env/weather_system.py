"""
env/weather_system.py
=====================
Derives weather state from CHIRPS rainfall values.
No external dataset needed — uses rainfall_stack already loaded.
"""

import numpy as np
from configs.config import (
    WEATHER_SUNNY, WEATHER_RAINY,
    RAINFALL_SUNNY_THRESH, N_SEASONS
)


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
        # Advance season every 80 steps (500 steps / 6 seasons ≈ 83)
        self.current_season = (timestep // 83) % N_SEASONS

        if rainfall_value < RAINFALL_SUNNY_THRESH:
            self.weather_state = WEATHER_SUNNY
            self.solar_rate    = 0.0015 * (1.0 - rainfall_value / RAINFALL_SUNNY_THRESH)
            self.extra_drain   = 0.0
        else:
            self.weather_state = WEATHER_RAINY
            self.solar_rate    = 0.0
            self.extra_drain   = 0.002  # extra battery drain in rain

    def is_rainy(self) -> bool:
        return self.weather_state == WEATHER_RAINY

    def is_sunny(self) -> bool:
        return self.weather_state == WEATHER_SUNNY

    def get_state_vector(self) -> np.ndarray:
        """Returns [weather_norm, solar_rate, extra_drain] as float32."""
        return np.array([
            float(self.weather_state),
            self.solar_rate / 0.0015,   # normalised
            self.extra_drain / 0.002,   # normalised
        ], dtype=np.float32)
