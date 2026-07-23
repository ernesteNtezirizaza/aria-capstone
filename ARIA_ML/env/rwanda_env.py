"""
env/rwanda_env.py
=================
RwandaReforestEnv — Complete ARIA Gymnasium environment.

Implements the full MDP with:
  1. Unified Planner + Navigator (one PPO policy)
  2. Solar + Battery energy system
  3. Weather system from CHIRPS data
  4. Rain cover mechanism
  5. Obstacle detection and avoidance
  6. Mission abort and return-to-base
  7. Seed monitoring and reseeding memory
  8. Full drone state machine (7 states)
  9. All 47 discrete actions
  10. 7-component observation space + terrain_stats
"""

import os, sys
import numpy as np
import gymnasium as gym
from gymnasium import spaces
from typing import Optional, Tuple, Dict

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from configs.config import *
from env.growth_engine      import GrowthEngine
from env.disturbance_engine import DisturbanceEngine
from env.monitoring_system  import MonitoringSystem
from env.weather_system     import WeatherSystem
from env.energy_system      import EnergySystem
from env.reward_function    import RewardFunction


class RwandaReforestEnv(gym.Env):
    """
    Full autonomous drone reforestation environment for Rwanda.

    STATE (8 components — terrain_stats added for generalisation)
    -------------------------------------------------------------
    terrain_window  (11,11,5) — local ecological terrain patch
    drone_vector    (10,)     — position + energy + weather + status
    coverage_map    (120,120,1) — seeded cells memory
    lifecycle_map   (120,120,1) — seed growth stages
    disturbance_map (120,120,1) — wildlife risk
    obstacle_map    (120,120,1) — terrain + airspace hazards
    mission_vector  (14,)     — zone quality + mission context, including
                                relative direction/distance to the nearest
                                queued reseed target (was an 8-dim count-only
                                vector with no positional signal) and, as of
                                the coverage-guidance feature, relative
                                direction/distance to the nearest unseeded
                                suitable cell (was an 11-dim vector with no
                                signal for where genuinely new ground is)
    terrain_stats   (6,)      — NEW: global terrain features for
                                generalisation across unseen zones

    ACTION SPACE: Discrete(47)
    --------------------------
    0-39  move(8) × seed(5 species)
    40    hover
    41    abort → return to base
    42    deploy rain cover
    43    retract rain cover
    44    increase altitude (obstacle avoidance)
    45    decrease altitude
    46    emergency land

    DRONE STATE MACHINE
    -------------------
    GROUNDED → TAKEOFF → NAVIGATING → SEEDING ↔ OBSTACLE
    SEEDING → RETURNING → LANDING → GROUNDED

    """

    metadata = {"render_modes": ["rgb_array"]}

    def __init__(
        self,
        zone_id:        Optional[int] = None,
        split:          str = "train",
        reward_weights: Optional[dict] = None,
        seed:           Optional[int] = None,
        species_recommender = None,
        min_seed_spacing: Optional[float] = None,
    ):
        super().__init__()
        self.zone_id        = zone_id   # array index 0..n_zones-1, or None for random
        self.split          = split
        self.reward_weights = reward_weights
        self._seed          = seed
        self.rng            = np.random.default_rng(seed)
        self._min_seed_spacing = min_seed_spacing

        # Load zone data
        self._load_zones()

        # ── Observation space ──────────────────────────────────────
        # terrain_stats (6,) is new — global features for generalisation
        self.observation_space = spaces.Dict({
            "terrain_window":  spaces.Box(0.0, 1.0,
                (OBS_WINDOW, OBS_WINDOW, N_CHANNELS), np.float32),
            "drone_vector":    spaces.Box(0.0, 1.0, (10,), np.float32),
            "coverage_map":    spaces.Box(0.0, 1.0,
                (ZONE_SIZE, ZONE_SIZE, 1), np.float32),
            "lifecycle_map":   spaces.Box(-1.0, 1.0,
                (ZONE_SIZE, ZONE_SIZE, 1), np.float32),
            "disturbance_map": spaces.Box(0.0, 1.0,
                (ZONE_SIZE, ZONE_SIZE, 1), np.float32),
            "obstacle_map":    spaces.Box(0.0, 1.0,
                (ZONE_SIZE, ZONE_SIZE, 1), np.float32),
            "mission_vector":  spaces.Box(0.0, 1.0, (14,), np.float32),
            # NEW — terrain statistics for cross-zone generalisation
            "terrain_stats":   spaces.Box(0.0, 1.0, (6,), np.float32),
        })

        # Action space: 47 discrete actions
        self.action_space = spaces.Discrete(N_ACTIONS)

        # Background systems
        self.growth      = GrowthEngine(ZONE_SIZE, self.rng)
        self.disturbance = DisturbanceEngine(self.rng)
        self.monitor     = MonitoringSystem(recommender=species_recommender)
        self.weather     = WeatherSystem()
        self.energy      = EnergySystem()
        self.reward_fn   = RewardFunction(reward_weights, min_seed_spacing=self._min_seed_spacing)

        # Episode state
        self._init_episode_state()

    # ── Data loading ───────────────────────────────────────────────
    def _load_zones(self):
        p = ZONES_DIR
        self.all_terrain  = np.nan_to_num(
            np.load(os.path.join(p, f"{self.split}_terrain.npy")),
            nan=0.0, posinf=1.0, neginf=0.0).astype(np.float32)
        self.all_dist     = np.nan_to_num(
            np.load(os.path.join(p, f"{self.split}_disturbance.npy")),
            nan=0.0, posinf=1.0, neginf=0.0).astype(np.float32)
        self.all_obs      = np.nan_to_num(
            np.load(os.path.join(p, f"{self.split}_obstacle.npy")),
            nan=0.0, posinf=1.0, neginf=0.0).astype(np.float32)
        self.all_rain     = np.nan_to_num(
            np.load(os.path.join(p, f"{self.split}_rainfall.npy")),
            nan=0.0, posinf=1.0, neginf=0.0).astype(np.float32)
        self.all_noplant  = np.load(
            os.path.join(p, f"{self.split}_noplant.npy"))
        self.n_zones      = self.all_terrain.shape[0]

        # Will be set per episode
        self.terrain = self.dist_grid = self.obs_grid = None
        self.rain_stack = self.no_plant = None

    def _init_episode_state(self):
        self.x = self.y          = ZONE_SIZE // 2
        self.altitude            = 1.0
        self.seeds_remaining     = INITIAL_SEEDS
        self.timestep            = 0
        self.season              = 0
        self.cover_deployed      = False
        self.drone_state         = STATE_GROUNDED
        self.episode_reward      = 0.0
        self.coverage_map        = np.zeros((ZONE_SIZE, ZONE_SIZE), np.float32)
        self.species_counts      = {i: 0 for i in range(N_SPECIES)}
        self.missions_completed  = 0
        self.obstacles_avoided   = 0
        self.abort_triggered     = False
        # Recognizing "this zone is bad, I should head back" is genuinely
        # useful information once. It should not pay out again for
        # repeating the same observation. Confirmed directly by testing:
        # without this flag, spamming ABORT in a below-threshold zone
        # nets +0.6 reward per action, forever, at zero cost -- no seeds
        # spent, no travel required, no risk. This is what was inflating
        # landings to 114,000+ in the last real training run (Exp 04/05).
        self.valid_abort_rewarded = False
        self.base_x              = ZONE_SIZE // 2
        self.base_y              = ZONE_SIZE // 2
        self.active_zone_idx     = None
        self.reseeding_targets   = {}  # (y, x) -> recommended species_id

    # ── Gymnasium API ──────────────────────────────────────────────
    def reset(self, seed=None, options=None):
        super().reset(seed=seed)
        rng_seed = int(self.np_random.integers(0, 2**31 - 1))
        self.rng = np.random.default_rng(rng_seed)

        # Reseed systems
        self.growth.rng      = self.rng
        self.disturbance.rng = self.rng

        if self.zone_id is None:
            # Domain randomisation: new zone every episode
            self.active_zone_idx = int(self.rng.integers(0, self.n_zones))
        else:
            # Fixed zone for evaluation — clamp to valid range
            self.active_zone_idx = int(
                np.clip(int(self.zone_id), 0, self.n_zones - 1)
            )

        self.terrain    = self.all_terrain[self.active_zone_idx].copy()
        self.dist_grid  = self.all_dist[self.active_zone_idx].copy()
        self.obs_grid   = self.all_obs[self.active_zone_idx].copy()
        self.rain_stack = self.all_rain[self.active_zone_idx].copy()
        self.no_plant   = self.all_noplant[self.active_zone_idx].copy()

        # Precomputed once per episode, reusing the exact same definition
        # used for the real pct_suitable_seeded ceiling (see the metrics
        # function below) -- a cell counts as achievable if it clears
        # ZONE_MIN_SOIL and the loosest species' rain_min during its best
        # month, not just whatever the current season happens to be. This
        # is static per episode (terrain doesn't change mid-episode), so
        # computing it once here and reusing it every step is both
        # correct and cheap, rather than recomputing it 1000+ times.
        soil_layer_ep = self.terrain[:, :, 2]
        rain_layer_ep = self.rain_stack.max(axis=0)
        min_rain_req_ep = min(sp["rain_min"] for sp in SPECIES.values())
        self.suitable_mask = (
            (~self.no_plant)
            & (self.dist_grid < 0.9)
            & (soil_layer_ep >= ZONE_MIN_SOIL)
            & (rain_layer_ep >= min_rain_req_ep)
        )

        self._init_episode_state()
        self.x, self.y = self._valid_start()
        self.base_x, self.base_y = self.x, self.y

        # Reset systems
        self.growth.reset(preserve_positions=set(self.monitor.pending_reseeds.keys()))
        self.disturbance.reset()
        self.monitor.reset()
        self.weather.reset()
        self.energy.reset()
        self.reward_fn.reset()

        self.drone_state = STATE_SEEDING

        obs  = self._obs()
        info = {"zone_idx": self.active_zone_idx,
                "start": (self.x, self.y)}
        return obs, info

    def step(self, action: int):
        assert self.action_space.contains(action)

        total_r = 0.0
        info    = {"action": int(action)}

        # ── Update weather + energy ────────────────────────────────
        rain_val = float(self.rain_stack[self.season, self.y, self.x])
        self.weather.step(rain_val, self.timestep)
        steps_to_base = max(abs(self.base_x - self.x), abs(self.base_y - self.y))
        energy_info = self.energy.step(self.weather, steps_to_base=steps_to_base)
        self.season = self.weather.current_season

        # Update rainfall channel
        self.terrain[:, :, 3] = self.rain_stack[self.season]

        # ── Handle special actions ─────────────────────────────────

        if action == EMERGENCY or energy_info["is_critical"]:
            if action == EMERGENCY and not energy_info["is_critical"]:
                total_r += 0.0      # voluntary EMERGENCY: punishment = lost future seeding reward
            else:
                total_r += self.reward_fn.battery_empty()  # genuine battery death
            info["emergency_land"]   = True
            info["episode_metrics"]  = self._metrics()
            info["growth_summary"]   = self.growth.summary()
            terminated = True
            truncated  = False
            self.timestep += 1
            return self._obs(), float(total_r), terminated, truncated, info

        if action == ABORT_ACTION:
            zone_score = self._zone_suitability()
            if zone_score < ZONE_MIN_SUITABILITY:
                if not self.valid_abort_rewarded:
                    total_r += self.reward_fn.battery_save()
                    self.valid_abort_rewarded = True
                info["valid_abort"] = True
            else:
                total_r += -1.0     # bad abort: small penalty, punishment = lost future reward
                info["bad_abort"] = True
            self.drone_state  = STATE_RETURNING
            self.abort_triggered = True

        elif action == COVER_DEPLOY:
            self.cover_deployed = True
            total_r += (self.reward_fn.w["cover_correct"]
                        if self.weather.is_rainy()
                        else self.reward_fn.w["cover_wrong"])

        elif action == COVER_RETRACT:
            self.cover_deployed = False
            total_r += (self.reward_fn.w["cover_correct"]
                        if self.weather.is_sunny()
                        else self.reward_fn.w["cover_wrong"])

        elif action == ALT_UP:
            was_blocked = (self.altitude < 0.5 and
                           self.obs_grid[self.y, self.x] >= 0.7)
            self.altitude = min(1.0, self.altitude + 0.1)
            if was_blocked:
                total_r += self.reward_fn.obstacle_clear()
                self.obstacles_avoided += 1
                self.drone_state = STATE_SEEDING
                info["obstacle_cleared"] = True
            # else: no reward for unnecessary altitude increase

        elif action == ALT_DOWN:
            self.altitude = max(0.0, self.altitude - 0.1)

        elif action == HOVER_ACTION:
            total_r += self.reward_fn.hover_penalty()

        else:
            # Movement + seed drop (actions 0-39)
            dir_idx    = action // N_SPECIES
            species_id = action % N_SPECIES
            dy, dx     = DIRECTIONS[dir_idx]

            # Reliable outbound flight to a queued reseed target: which
            # failure to prioritize and which species to use there were
            # already fully decided (SpeciesRecommender + priority queue,
            # computed at the moment of failure) -- physically getting back
            # to that exact remembered cell was the one piece with zero
            # validated successes across ~5,000 real opportunities this
            # session. Scripting the movement here is the same treatment
            # return-to-base already gets, not a new kind of assistance,
            # just the same reliability applied to the other half of the
            # same round trip. Obstacle avoidance below still applies
            # normally on this scripted path, same as any other movement.
            if self.reseeding_targets and self.drone_state == STATE_SEEDING:
                ty, tx = min(self.reseeding_targets,
                             key=lambda t: abs(t[0] - self.y) + abs(t[1] - self.x))
                dx = int(np.sign(tx - self.x))
                dy = int(np.sign(ty - self.y))

            new_x = int(np.clip(self.x + dx, 0, ZONE_SIZE - 1))
            new_y = int(np.clip(self.y + dy, 0, ZONE_SIZE - 1))

            if self.obs_grid[new_y, new_x] > 0.7 and self.altitude < 0.5:
                total_r += self.reward_fn.obstacle_hit()
                self.drone_state = STATE_OBSTACLE
                info["obstacle_hit"] = True
            else:
                # The dense per-step shaping reward that used to live here
                # was removed. It rewarded reducing distance to a queued
                # reseed target every step, meant to teach the policy to
                # navigate there before that navigation was scripted
                # (above). With navigation guaranteed regardless of reward,
                # the shaping payout became a reliable, repeatable reward
                # loop with no remaining teaching purpose, and real data
                # showed it pulling the policy toward cycling back to known
                # targets instead of covering new ground: reseed outcomes
                # went from 0 to over 5,800 in one run while
                # pct_suitable_seeded dropped 6 to 9x at the same time.
                # w_reseed (reward_function.py) still rewards completing a
                # correction once it happens.
                self.x, self.y = new_x, new_y

            if (self.drone_state == STATE_SEEDING
                    and self.seeds_remaining > 0):

                # If the drone has arrived at a queued reseed target, use
                # the species already recommended for that specific failed
                # cell, computed by SpeciesRecommender when the failure was
                # first recorded, rather than the per-step action's species
                # choice, which was never meant to relitigate a decision
                # that's already been made.
                recommended = self.reseeding_targets.get((self.y, self.x))
                if recommended is not None:
                    species_id = recommended

                soil  = float(self.terrain[self.y, self.x, 2])
                rain  = float(self.rain_stack[self.season, self.y, self.x])
                slope = float(self.terrain[self.y, self.x, 1]) * 90.0
                prox  = float(self.dist_grid[self.y, self.x])
                in_p  = prox >= 0.9
                no_p  = bool(self.no_plant[self.y, self.x])

                soil  = 0.0 if np.isnan(soil)  else soil
                rain  = 0.0 if np.isnan(rain)  else rain
                prox  = 0.0 if np.isnan(prox)  else prox

                rain_min   = SPECIES[species_id]["rain_min"]
                is_suitable = (not no_p and not in_p
                               and rain >= rain_min and soil >= ZONE_MIN_SOIL)

                is_reseed  = (self.y, self.x) in self.reseeding_targets

                # Restored as part of reverting to Run A's proven
                # configuration (55.8% average seeding efficiency, the
                # best result this session) -- see the matching comment
                # in env/reward_function.py for the full history of why.
                already_covered = bool(self.coverage_map[self.y, self.x] > 0)
                if not is_reseed:
                    if already_covered:
                        total_r += REWARD["w_redundant_penalty"]
                    elif is_suitable:
                        total_r += REWARD["w_new_coverage_bonus"]

                r, breakdown = self.reward_fn.placement(
                    self.x, self.y, species_id,
                    soil, rain, slope, prox, in_p,
                    is_hover=False,
                    cover_deployed=self.cover_deployed,
                    is_rainy=self.weather.is_rainy(),
                    is_suitable=is_suitable,
                    is_reseeding_target=is_reseed,
                )
                total_r += r

                self.growth.register(
                    species_id, self.x, self.y, self.timestep,
                    soil, rain, float(self.terrain[self.y, self.x, 1]),
                    prox, is_suitable, in_p
                )
                self.coverage_map[self.y, self.x] = 1.0
                self.species_counts[species_id]   += 1
                self.seeds_remaining               -= 1

                if is_reseed:
                    self.monitor.mark_reseeded(self.x, self.y)
                    self.reseeding_targets.pop((self.y, self.x), None)

                info["seed_dropped"]  = True
                info["is_suitable"]   = is_suitable
                info["breakdown"]     = breakdown

        # ── Return to base logic ───────────────────────────────────
        if (energy_info["should_return"] and
                self.drone_state == STATE_SEEDING):
            self.drone_state = STATE_RETURNING
            total_r += self.reward_fn.battery_save()
            info["returning_battery"] = True

        if self.drone_state == STATE_RETURNING:
            dx = np.sign(self.base_x - self.x)
            dy = np.sign(self.base_y - self.y)
            self.x = int(np.clip(self.x + dx, 0, ZONE_SIZE - 1))
            self.y = int(np.clip(self.y + dy, 0, ZONE_SIZE - 1))

            if self.x == self.base_x and self.y == self.base_y:
                # BUG FIX: this used to set STATE_LANDING and never transition
                # back to STATE_SEEDING anywhere in the file. Since seed
                # placement (env/rwanda_env.py's movement+drop branch) is
                # gated on drone_state == STATE_SEEDING, that meant the drone
                # became permanently unable to place ANY seed, including a
                # reseed attempt, for the rest of the episode the moment it
                # first returned to base -- confirmed directly: 50 steps of
                # every possible action after a forced landing produced zero
                # seed drops. This is a more fundamental, upstream explanation
                # for reseed_attempts staying at 0 than anything navigation-
                # related: the drone could never drop ANY seed again after
                # its first landing, so it could never have succeeded at a
                # reseed regardless of how well it navigated.
                self.drone_state = STATE_SEEDING
                self.energy.recharge(0.5)
                self.missions_completed += 1
                targets = self.monitor.get_top_targets(3)
                for t in targets:
                    self.reseeding_targets[(t["y"], t["x"])] = t.get("recommended_species")
                # Diagnostic: confirms the scripted return-to-base handoff
                # itself fires, and whether the queue actually had entries
                # waiting at this exact moment, before any outbound flight
                # toward a target has even started.
                self.monitor.recommender.landings_completed += 1
                if targets:
                    self.monitor.recommender.landings_with_targets += 1
                info["landed"] = True

        # ── Monitoring step ────────────────────────────────────────
        if self.timestep % MONITORING_INTERVAL == 0 and self.timestep > 0:
            rain_map = self.rain_stack[self.season]
            growth_events, gr = self.growth.step(self.timestep, rain_map)
            _, dr    = self.disturbance.step(self.growth, self.timestep)
            total_r += gr + dr

            # Close the reseed feedback loop: any seed that matured this
            # step, at a position that was a pending reseed, is a real
            # success outcome for whichever species SpeciesRecommender
            # picked there -- feed it back before ingesting new failures.
            matured_positions = [(e["x"], e["y"]) for e in growth_events
                                  if e.get("type") == "mature"]
            self.monitor.resolve_matured(matured_positions)

            self.monitor.ingest_failures(self.growth.failed_cells.copy())
            self.growth.failed_cells.clear()

        # ── Timestep ───────────────────────────────────────────────
        total_r             += -REWARD["step_penalty"]
        self.timestep        += 1
        self.episode_reward  += total_r

        terminated = (self.seeds_remaining <= 0
                      or energy_info["is_critical"])
        truncated  = self.timestep >= MAX_STEPS

        if terminated or truncated:
            info["episode_metrics"] = self._metrics()
            info["growth_summary"]  = self.growth.summary()
            info["energy_summary"]  = self.energy.get_summary()
            info["monitor_summary"] = self.monitor.summary()

        return self._obs(), float(total_r), terminated, truncated, info

    # ── Helpers ────────────────────────────────────────────────────
    def _valid_start(self):
        for _ in range(100):
            x = int(self.rng.integers(5, ZONE_SIZE - 5))
            y = int(self.rng.integers(5, ZONE_SIZE - 5))
            if (not self.no_plant[y, x]
                    and self.dist_grid[y, x] < 0.9):
                return x, y
        return ZONE_SIZE // 2, ZONE_SIZE // 2

    def _zone_suitability(self) -> float:
        """
        Composite zone-level suitability, combining soil, rainfall, and
        slope with the exact same weights (ZONE_SUITABILITY_WEIGHTS) used
        for per-seed placement reward in reward_function.py. Previously
        this only averaged the soil channel, so the abort decision and
        mission_vector's zone_score ignored rain and slope entirely even
        though the reward function already scored all three. Compared
        against ZONE_MIN_SUITABILITY (not ZONE_MIN_SOIL).
        """
        soil  = float(np.nanmean(self.terrain[:, :, CH_SOIL]))
        rain  = float(np.nanmean(self.rain_stack[self.season]))
        slope_deg = float(np.nanmean(self.terrain[:, :, CH_SLOPE])) * 90.0
        slope_pen = min(slope_deg / MAX_SLOPE_DEG, 1.0)

        w = ZONE_SUITABILITY_WEIGHTS
        score = (w["soil"]*soil + w["rain"]*rain - w["slope"]*slope_pen) \
                / (w["soil"] + w["rain"] + w["slope"])
        return float(np.clip(score, 0.0, 1.0))

    def _sanitise(self, arr):
        return np.nan_to_num(
            arr, nan=0.0, posinf=1.0, neginf=0.0
        ).astype(np.float32)

    def _terrain_stats(self) -> np.ndarray:
        """
        Compute 6 normalised terrain statistics for the *active* zone,
        using the already-sliced self.terrain/rain_stack/no_plant arrays
        (active_zone_idx itself gets reset to None by _init_episode_state,
        so we read the sliced arrays directly rather than re-indexing by
        zone id). See zone_terrain_stats() below for scoring arbitrary,
        not-yet-loaded zones (used by the zone selector).

        All 5 terrain channels (elevation, slope, soil, rainfall,
        landcover) are already normalised to [0, 1] by utils/preprocess.py
        before being saved, so they're used directly here -- NOT divided
        again by 3000 / 10. (A previous version of this function did divide
        elevation by 3000 and landcover by 10 a second time, which silently
        crushed both features to near-zero regardless of the real terrain,
        making 2 of the agent's 6 generalisation features useless. Fixed.)

        Features:
          0 — mean elevation  (0-1, already normalised)
          1 — mean slope      (0-1, already normalised by 90 degrees)
          2 — mean soil score (0-1)
          3 — mean rainfall   (0-1)
          4 — mean landcover  (0-1, already normalised)
          5 — fraction of plantable cells (no_plant=False)
        """
        elev  = float(self.terrain[:, :, CH_ELEVATION].mean())
        slope = float(self.terrain[:, :, CH_SLOPE].mean())
        soil  = float(self.terrain[:, :, CH_SOIL].mean())
        rain  = float(self.terrain[:, :, CH_RAINFALL].mean())
        lc    = float(self.terrain[:, :, CH_LANDCOVER].mean())
        plant = float((~self.no_plant.astype(bool)).mean())

        stats = np.array([elev, slope, soil, rain, lc, plant], dtype=np.float32)
        return np.clip(stats, 0.0, 1.0)

    def zone_terrain_stats(self, zone_idx: int) -> np.ndarray:
        """
        Same 6 features as _terrain_stats(), but computed for ANY zone in
        this split from the full all_terrain/all_rain/all_noplant arrays,
        not just the currently active one. This is what lets a zone
        selector (env/zone_selector.py) score every candidate zone
        *before* the drone is actually deployed to one of them.
        """
        terrain  = self.all_terrain[zone_idx]
        rain     = self.all_rain[zone_idx]
        no_plant = self.all_noplant[zone_idx]

        elev  = float(terrain[:, :, CH_ELEVATION].mean())
        slope = float(terrain[:, :, CH_SLOPE].mean())
        soil  = float(terrain[:, :, CH_SOIL].mean())
        rainv = float(rain.mean())
        lc    = float(terrain[:, :, CH_LANDCOVER].mean())
        plant = float((~no_plant.astype(bool)).mean())

        stats = np.array([elev, slope, soil, rainv, lc, plant], dtype=np.float32)
        return np.clip(stats, 0.0, 1.0)

    def available_zone_stats(self) -> np.ndarray:
        """terrain_stats for every zone in this split -- shape (n_zones, 6)."""
        return np.stack([self.zone_terrain_stats(i) for i in range(self.n_zones)])

    def _nearest_reseed_offset(self, x: int, y: int):
        """
        Returns (rel_dy, rel_dx, manhattan_dist_norm) to the nearest queued
        reseed target from position (x, y), or (0.0, 0.0, 1.0) — a neutral
        "no target" sentinel — if none are queued. Shared by _obs() (so the
        policy can perceive direction/distance) and the dense distance-
        shaping reward in step() (so movement toward a target is rewarded
        incrementally, not only at the moment of actually landing on it).
        """
        if not self.reseeding_targets:
            return 0.0, 0.0, 1.0
        ty, tx = min(self.reseeding_targets,
                     key=lambda t: abs(t[0] - y) + abs(t[1] - x))
        rel_dy = (ty - y) / ZONE_SIZE
        rel_dx = (tx - x) / ZONE_SIZE
        manhattan_dist = (abs(ty - y) + abs(tx - x)) / (2 * ZONE_SIZE)
        return rel_dy, rel_dx, manhattan_dist

    def _nearest_unseeded_suitable_offset(self, x: int, y: int):
        """
        Returns (rel_dy, rel_dx, manhattan_dist_norm) to the nearest cell
        that is both genuinely suitable (self.suitable_mask) and not yet
        seeded (self.coverage_map == 0), or a neutral (0.0, 0.0, 1.0)
        sentinel if none remain. This is the same idea that fixed reseed
        navigation (_nearest_reseed_offset above): before that fix, the
        policy could only perceive a queued target by chance exploration
        of spatial grids via the CNN, never a direct signal to steer by,
        and reseed success stayed near zero until direction/distance were
        added explicitly. Genuine zone coverage has the same shape of
        problem -- the coverage_map and terrain grids require the CNN to
        infer "where is unexplored, suitable ground" spatially, rather
        than being told directly. Uses vectorized numpy rather than a
        Python-level search, since the reseed target set is small (tens
        of entries) but unseeded suitable ground can be thousands of
        cells early in an episode, too many for a per-step Python loop.
        """
        unseeded_suitable = self.suitable_mask & (self.coverage_map == 0)
        coords = np.argwhere(unseeded_suitable)
        if coords.shape[0] == 0:
            return 0.0, 0.0, 1.0
        dists = np.abs(coords[:, 0] - y) + np.abs(coords[:, 1] - x)
        nearest_idx = int(np.argmin(dists))
        ty, tx = coords[nearest_idx]
        rel_dy = (ty - y) / ZONE_SIZE
        rel_dx = (tx - x) / ZONE_SIZE
        manhattan_dist = (abs(ty - y) + abs(tx - x)) / (2 * ZONE_SIZE)
        return float(rel_dy), float(rel_dx), float(manhattan_dist)

    def _obs(self) -> Dict:
        half = OBS_WINDOW // 2
        padded = np.pad(self.terrain,
                        ((half, half), (half, half), (0, 0)), mode="edge")
        window = padded[self.y:self.y + OBS_WINDOW,
                        self.x:self.x + OBS_WINDOW]

        dist_base = float(
            np.sqrt((self.x - self.base_x) ** 2
                    + (self.y - self.base_y) ** 2) / (ZONE_SIZE * 1.4)
        )

        drone_vec = np.array([
            self.x / (ZONE_SIZE - 1),
            self.y / (ZONE_SIZE - 1),
            self.altitude,
            self.energy.get_state(),
            self.energy.solar_input / 0.0015,
            self.seeds_remaining / INITIAL_SEEDS,
            float(self.weather.weather_state),
            float(self.cover_deployed),
            dist_base,
            self.drone_state / 6.0,
        ], dtype=np.float32)

        zone_score  = self._zone_suitability()
        rain_mean   = float(self.rain_stack[self.season].mean())
        slope_pct   = float(self.no_plant.mean())
        covered_pct = float(self.coverage_map.mean())
        failed_n    = min(self.monitor.queue_size() / 50.0, 1.0)
        reseed_n    = min(len(self.reseeding_targets) / 10.0, 1.0)
        abort_score = float(zone_score < ZONE_MIN_SUITABILITY)
        is_reseed   = float(len(self.reseeding_targets) > 0)

        # Previously the policy only ever saw a COUNT of queued reseed
        # targets (reseed_n above), never where they actually are. It had
        # no way to learn "navigate back to a failed cell" -- it could only
        # stumble onto one by chance during ordinary exploration, which is
        # why SHARED_SPECIES_RECOMMENDER.n_updates stayed at 0 across full
        # 200k-timestep training runs. Adding the nearest target's relative
        # position gives the policy an actual signal to steer by, the same
        # way it already has for terrain features.
        rel_dy, rel_dx, manhattan_dist = self._nearest_reseed_offset(self.x, self.y)

        reseed_dy   = np.clip((rel_dy + 1.0) / 2.0, 0.0, 1.0)  # 0.5 = same row as drone
        reseed_dx   = np.clip((rel_dx + 1.0) / 2.0, 0.0, 1.0)  # 0.5 = same column as drone
        reseed_dist = np.clip(manhattan_dist, 0.0, 1.0)

        # Same idea, applied to genuine coverage instead of reseed
        # correction: direction/distance to the nearest cell that is both
        # suitable and not yet seeded, computed once per step from the
        # per-episode suitable_mask precomputed in reset(). Without this,
        # the policy has no more direct way to find unexplored suitable
        # ground than it had to find a reseed target before that feature
        # was added -- which is exactly the gap that kept reseed success
        # near zero until it was closed the same way.
        cov_rel_dy, cov_rel_dx, cov_dist = self._nearest_unseeded_suitable_offset(self.x, self.y)
        coverage_dy   = np.clip((cov_rel_dy + 1.0) / 2.0, 0.0, 1.0)
        coverage_dx   = np.clip((cov_rel_dx + 1.0) / 2.0, 0.0, 1.0)
        coverage_dist = np.clip(cov_dist, 0.0, 1.0)

        mission_vec = np.array([
            zone_score, rain_mean, covered_pct,
            failed_n, reseed_n, abort_score,
            self.missions_completed / 10.0,
            is_reseed,
            reseed_dy, reseed_dx, reseed_dist,
            coverage_dy, coverage_dx, coverage_dist,
        ], dtype=np.float32)

        lc = self.growth.lifecycle_map()

        return {
            "terrain_window":  self._sanitise(window),
            "drone_vector":    np.clip(drone_vec, 0.0, 1.0),
            "coverage_map":    self._sanitise(self.coverage_map[:, :, np.newaxis]),
            "lifecycle_map":   self._sanitise(lc[:, :, np.newaxis]),
            "disturbance_map": self._sanitise(self.dist_grid[:, :, np.newaxis]),
            "obstacle_map":    self._sanitise(self.obs_grid[:, :, np.newaxis]),
            "mission_vector":  np.clip(mission_vec, 0.0, 1.0),
            "terrain_stats":   self._terrain_stats(),
        }

    def _metrics(self) -> dict:
        seeds = list(self.growth.seeds.values())
        if not seeds:
            return {m: 0.0 for m in EVAL_METRICS}

        # n_suit previously only checked "not blocked, not protected", a
        # much looser bar than is_suitable actually requires (also soil
        # above ZONE_MIN_SOIL and rain above a species' rain_min). That
        # meant the ceiling counted nearly every plantable cell as
        # achievable -- roughly the whole non-protected zone -- when a
        # huge share of those cells could never pass the real suitability
        # check no matter how good the policy is. The ceiling was never
        # truly reachable, which is exactly why efficiency numbers looked
        # stuck in the 30-40% range even as absolute performance improved.
        # Fixed to use the identical four conditions is_suitable checks,
        # and the most lenient species' rain_min (a smart policy picks
        # whichever species matches a given cell, so a cell only needs to
        # clear the EASIEST species' bar to genuinely be achievable).
        soil_layer  = self.terrain[:, :, 2]
        # Uses each cell's BEST month across the full year (self.rain_stack
        # has one layer per month), not just whatever single season happens
        # to be active the instant this runs. Checking only self.season
        # here was a real bug: if that one sampled month was unusually dry
        # for a zone, the suitable-cell count could collapse toward zero
        # while real seed placements, made across many different months
        # during the actual episode, stayed legitimately high -- producing
        # mathematically impossible ratios (a real run showed 9.763 for
        # one zone, where a fraction can never exceed 1.0). A cell counts
        # as achievable here if it could be suitable during its best month,
        # since a smart policy can time a visit to any cell accordingly.
        rain_layer  = self.rain_stack.max(axis=0)
        min_rain_req = min(sp["rain_min"] for sp in SPECIES.values())
        real_suitable_mask = (
            (~self.no_plant)
            & (self.dist_grid < 0.9)
            & (soil_layer >= ZONE_MIN_SOIL)
            & (rain_layer >= min_rain_req)
        )
        n_suit   = int(real_suitable_mask.sum())
        n_seeded = sum(1 for s in seeds if s.is_suitable)
        # Clamped defensively: pct_suitable_seeded is a fraction of suitable
        # cells and can never legitimately exceed 1.0. Without this, an
        # underestimated n_suit for any reason produces a mathematically
        # impossible value (a real run showed 9.763 for one zone before
        # the rain_layer fix above) instead of a clear, bounded signal
        # that something needs investigating.
        pct      = min(1.0, n_seeded / max(n_suit, 1))

        # pct_suitable_seeded's denominator is every suitable cell in the
        # WHOLE zone, but the drone only carries INITIAL_SEEDS per episode,
        # so even perfect placement of every seed can't exceed this ceiling.
        # seeding_efficiency = how close the policy got to that ceiling,
        # which is the more honest number for comparing zones/experiments
        # against each other (raw pct_suitable_seeded will always look
        # small if n_suit is large, regardless of policy quality).
        ceiling    = min(1.0, INITIAL_SEEDS / max(n_suit, 1))
        efficiency = pct / ceiling if ceiling > 0 else 0.0

        counts = np.array(list(self.species_counts.values()), dtype=float)
        counts = counts[counts > 0]
        H = (-np.sum((counts / counts.sum()) * np.log(counts / counts.sum()))
             if len(counts) > 1 else 0.0)

        pos  = [(s.x, s.y) for s in seeds]
        viol = sum(
            1 for i, (x1, y1) in enumerate(pos)
            for x2, y2 in pos[i + 1:]
            if abs(x1 - x2) + abs(y1 - y2) < MIN_SEED_SPACING
        )

        return {
            "pct_suitable_seeded":  round(pct, 4),
            "seeding_ceiling":      round(ceiling, 4),
            "seeding_efficiency":   round(min(efficiency, 1.0), 4),
            "mean_soil_score":      round(float(np.mean([s.soil_score for s in seeds])), 4),
            "species_entropy":      round(float(H), 4),
            "spacing_violations":   int(viol),
            "protected_area_seeds": int(sum(1 for s in seeds if s.in_protected)),
            "seasonal_rain_score":  round(float(np.mean([s.rain_score for s in seeds])), 4),
            "reseeding_count":      self.growth.summary()["reseeding_count"],
            "missions_completed":   self.missions_completed,
            "battery_empty_events": self.energy.empty_events,
            "obstacles_avoided":    self.obstacles_avoided,
        }

    def render(self, mode="rgb_array"):
        canvas = np.zeros((ZONE_SIZE, ZONE_SIZE, 3), dtype=np.uint8)
        soil   = np.nan_to_num(self.terrain[:, :, 2], nan=0.0)
        bg     = (soil * 180).astype(np.uint8)
        canvas[:, :, 0] = bg
        canvas[:, :, 1] = bg
        canvas[:, :, 2] = bg
        canvas[self.no_plant]              = [80,  80,  80]
        canvas[self.dist_grid > 0.5, 2]   = 200
        canvas[self.obs_grid  > 0.7, 0]   = 200
        lc = self.growth.lifecycle_map()
        for y in range(ZONE_SIZE):
            for x in range(ZONE_SIZE):
                v = lc[y, x]
                if   v == -1.0: canvas[y, x] = [180, 40, 40]
                elif v > 0:     canvas[y, x] = [20, int(100 + v * 155), 20]
        canvas[self.y, self.x] = [255, 220, 0]
        return canvas

    def close(self):
        pass