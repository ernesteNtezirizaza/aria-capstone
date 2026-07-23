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

    def step(self, weather_system, steps_to_base: int = 0) -> dict:
        """
        Update battery level for one timestep.

        Parameters
        ----------
        weather_system : WeatherSystem
            Current weather state drives drain rate and solar input.
        steps_to_base : int
            Chebyshev distance from the drone's current position to base
            (matches the diagonal scripted return movement in
            rwanda_env.py: dx/dy both close simultaneously each step, so
            steps needed = max(|dx|, |dy|), not Manhattan distance).
            Used to make should_return distance-aware -- see class
            docstring note below on why a fixed percentage threshold was
            a real bug, not just a simplification.

        Returns
        -------
        dict with keys:
          battery      — current battery level [0,1]
          solar_input  — energy gained from solar this step
          drain        — energy lost this step
          should_return— True if battery is below what's needed to
                         safely reach base from here, worst case
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

        # Distance-aware return trigger. A fixed BATTERY_RETURN_THRESH was
        # a real bug: the 5%-battery margin between "should return" and
        # "critical" only covers 12-25 steps of flight depending on
        # weather, but the drone can range far further than that from
        # base in a real zone. A drone that happened to be further out
        # than the margin allowed when the fixed threshold fired could
        # not physically survive the trip back, and would die to
        # battery-critical with most of its seed budget never used --
        # confirmed directly via instrumentation: under random actions,
        # every single episode ended this way, using under 4% of the
        # seed budget on average. This computes the real worst-case
        # energy needed for the ACTUAL distance back to base (assuming
        # rain the whole way, the safe assumption) plus a small buffer,
        # instead of a one-size-fits-all percentage that was only ever
        # safe for positions already close to base.
        safe_margin = steps_to_base * BATTERY_DRAIN_RAIN + BATTERY_CRITICAL
        return_thresh = max(BATTERY_RETURN_THRESH, safe_margin)

        return {
            "battery":       self.battery,
            "solar_input":   self.solar_input,
            "drain":         self.drain_this_step,
            "should_return": self.battery < return_thresh,
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
