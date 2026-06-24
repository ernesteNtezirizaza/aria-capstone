"""
env/energy_system.py
====================
Manages drone battery using solar charging from weather system.

When sunny  → solar panels charge the battery
When rainy  → battery drains faster, no solar generation
When empty  → forced emergency landing
"""

import numpy as np
from configs.config import (
    BATTERY_MAX, BATTERY_INIT,
    BATTERY_DRAIN_SUNNY, BATTERY_DRAIN_RAIN,
    SOLAR_CHARGE_RATE,
    BATTERY_RETURN_THRESH, BATTERY_CRITICAL
)


class EnergySystem:
    """
    Battery and solar energy management for the ARIA drone.

    The drone has two energy inputs:
      1. Solar panels — active when weather is sunny
      2. Battery      — always draining, rate doubles in rain

    When battery falls below BATTERY_RETURN_THRESH the planner
    is notified to recommend returning to base.

    When battery falls below BATTERY_CRITICAL the episode
    terminates with a forced emergency landing.
    """

    def __init__(self):
        self.battery       = BATTERY_INIT
        self.solar_input   = 0.0
        self.drain_this_step = 0.0
        self.total_solar   = 0.0
        self.total_drain   = 0.0
        self.empty_events  = 0

    def reset(self):
        self.battery         = BATTERY_INIT
        self.solar_input     = 0.0
        self.drain_this_step = 0.0
        self.total_solar     = 0.0
        self.total_drain     = 0.0
        self.empty_events    = 0

    def step(self, weather_system) -> dict:
        """
        Update battery level for one timestep.

        Parameters
        ----------
        weather_system : WeatherSystem
            Current weather state drives drain rate and solar input.

        Returns
        -------
        dict with keys:
          battery      — current battery level [0,1]
          solar_input  — energy gained from solar this step
          drain        — energy lost this step
          should_return— True if battery below return threshold
          is_critical  — True if battery below critical threshold
        """
        # Solar generation only when sunny
        if weather_system.is_sunny():
            self.solar_input = weather_system.solar_rate
        else:
            self.solar_input = 0.0

        # Drain rate doubles in rain
        if weather_system.is_rainy():
            drain = BATTERY_DRAIN_RAIN
        else:
            drain = BATTERY_DRAIN_SUNNY

        self.drain_this_step = drain
        self.battery = np.clip(
            self.battery + self.solar_input - drain,
            0.0, BATTERY_MAX
        )

        self.total_solar += self.solar_input
        self.total_drain += drain

        if self.battery <= BATTERY_CRITICAL:
            self.empty_events += 1

        return {
            "battery":       self.battery,
            "solar_input":   self.solar_input,
            "drain":         self.drain_this_step,
            "should_return": self.battery < BATTERY_RETURN_THRESH,
            "is_critical":   self.battery <= BATTERY_CRITICAL,
        }

    def recharge(self, amount: float = 1.0):
        """Called when drone lands at base to recharge."""
        self.battery = min(BATTERY_INIT, self.battery + amount)

    def get_state(self) -> float:
        """Normalised battery level [0,1]."""
        return float(self.battery / BATTERY_MAX)

    def get_summary(self) -> dict:
        return {
            "final_battery":    self.battery,
            "total_solar":      self.total_solar,
            "total_drain":      self.total_drain,
            "battery_empty_events": self.empty_events,
        }
