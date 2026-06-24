"""
env/rwanda_env.py
=================
RwandaReforestEnv — Complete ARIA V2 Gymnasium environment.

Implements the full MDP with:
  ✓ Unified Planner + Navigator (one PPO policy)
  ✓ Solar + Battery energy system
  ✓ Weather system from CHIRPS data
  ✓ Rain cover mechanism
  ✓ Obstacle detection and avoidance
  ✓ Mission abort and return-to-base
  ✓ Seed monitoring and reseeding memory
  ✓ Full drone state machine (7 states)
  ✓ All 47 discrete actions
  ✓ 7-component observation space
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

    STATE (7 components)
    --------------------
    terrain_window  (11,11,5) — local ecological terrain
    drone_vector    (10,)     — position + energy + weather + status
    coverage_map    (120,120,1) — seeded cells memory
    lifecycle_map   (120,120,1) — seed growth stages
    disturbance_map (120,120,1) — wildlife risk
    obstacle_map    (120,120,1) — terrain + airspace hazards
    mission_vector  (8,)      — zone quality + mission context

    ACTION SPACE: Discrete(47)
    --------------------------
    0-39  move(8) × seed(5 species)
    40    hover
    41    abort → return to base
    42    deploy rain cover
    43    retract rain cover
    44    increase altitude
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
    ):
        super().__init__()
        self.zone_id        = zone_id
        self.split          = split
        self.reward_weights = reward_weights
        self._seed          = seed
        self.rng            = np.random.default_rng(seed)

        # Load zone data
        self._load_zones()

        # Observation space
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
            "mission_vector":  spaces.Box(0.0, 1.0, (8,), np.float32),
        })

        # Action space: 47 discrete actions
        self.action_space = spaces.Discrete(N_ACTIONS)

        # Background systems
        self.growth      = GrowthEngine(ZONE_SIZE, self.rng)
        self.disturbance = DisturbanceEngine(self.rng)
        self.monitor     = MonitoringSystem()
        self.weather     = WeatherSystem()
        self.energy      = EnergySystem()
        self.reward_fn   = RewardFunction(reward_weights)

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
        self.altitude            = 1.0      # normalised [0,1]
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
        self.base_x              = ZONE_SIZE // 2
        self.base_y              = ZONE_SIZE // 2
        self.active_zone_idx     = None
        self.reseeding_targets   = set()

    # ── Gymnasium API ──────────────────────────────────────────────
    def reset(self, seed=None, options=None):
        super().reset(seed=seed)
        rng_seed = int(self.np_random.integers(0, 2**31 - 1))
        self.rng = np.random.default_rng(rng_seed)

        # Reseed systems
        self.growth.rng      = self.rng
        self.disturbance.rng = self.rng

        # Select zone
        if self.zone_id is not None:
            self.active_zone_idx = self.zone_id - 1
        else:
            self.active_zone_idx = int(self.rng.integers(0, self.n_zones))

        self.terrain    = self.all_terrain[self.active_zone_idx].copy()
        self.dist_grid  = self.all_dist[self.active_zone_idx].copy()
        self.obs_grid   = self.all_obs[self.active_zone_idx].copy()
        self.rain_stack = self.all_rain[self.active_zone_idx].copy()
        self.no_plant   = self.all_noplant[self.active_zone_idx].copy()

        self._init_episode_state()
        self.x, self.y = self._valid_start()
        self.base_x, self.base_y = self.x, self.y

        # Reset systems
        self.growth.reset()
        self.disturbance.reset()
        self.monitor.reset()
        self.weather.reset()
        self.energy.reset()
        self.reward_fn.reset()

        self.drone_state = STATE_SEEDING   # start ready to seed

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
        energy_info = self.energy.step(self.weather)
        self.season = self.weather.current_season

        # Update rainfall channel
        self.terrain[:, :, 3] = self.rain_stack[self.season]

        # ── Handle special actions ─────────────────────────────────

        if action == EMERGENCY or energy_info["is_critical"]:
            # Emergency land — battery critical
            total_r += self.reward_fn.battery_empty()
            info["emergency_land"] = True
            terminated = True
            truncated  = False
            self.timestep += 1
            return self._obs(), float(total_r), terminated, truncated, info

        if action == ABORT_ACTION:
            # Abort mission — return to base
            zone_score = self._zone_suitability()
            if zone_score < ZONE_MIN_SOIL:
                # Valid abort — zone is genuinely unsuitable
                total_r += self.reward_fn.battery_save()
                info["valid_abort"] = True
            else:
                # Unnecessary abort — zone was fine
                total_r += self.reward_fn.bad_abort()
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

        elif action == COVER_RETRACT:
            self.cover_deployed = False
            total_r += (self.reward_fn.reward_fn.w["cover_correct"]
                        if self.weather.is_sunny()
                        else self.reward_fn.reward_fn.w["cover_wrong"])

        elif action == ALT_UP:
            self.altitude = min(1.0, self.altitude + 0.1)
            # Check if obstacle cleared
            if self.obs_grid[self.y, self.x] < 0.5:
                total_r += self.reward_fn.obstacle_clear()
                self.obstacles_avoided += 1
                self.drone_state = STATE_SEEDING
                info["obstacle_cleared"] = True

        elif action == ALT_DOWN:
            self.altitude = max(0.0, self.altitude - 0.1)

        elif action == HOVER_ACTION:
            total_r += self.reward_fn.hover_penalty()

        else:
            # Movement + seed drop (actions 0-39)
            dir_idx    = action // N_SPECIES
            species_id = action % N_SPECIES
            dy, dx     = DIRECTIONS[dir_idx]

            new_x = int(np.clip(self.x + dx, 0, ZONE_SIZE - 1))
            new_y = int(np.clip(self.y + dy, 0, ZONE_SIZE - 1))

            # Obstacle check
            if self.obs_grid[new_y, new_x] > 0.7 and self.altitude < 0.5:
                total_r += self.reward_fn.obstacle_hit()
                self.drone_state = STATE_OBSTACLE
                info["obstacle_hit"] = True
                # Don't move — bounce back
            else:
                self.x, self.y = new_x, new_y

            # Seed drop if in seeding state and seeds available
            if (self.drone_state == STATE_SEEDING
                    and self.seeds_remaining > 0):

                soil  = float(self.terrain[self.y, self.x, 2])
                rain  = float(self.rain_stack[self.season, self.y, self.x])
                slope = float(self.terrain[self.y, self.x, 1]) * 90.0
                prox  = float(self.dist_grid[self.y, self.x])
                in_p  = prox >= 0.9
                no_p  = bool(self.no_plant[self.y, self.x])

                # Sanitise
                soil  = 0.0 if np.isnan(soil)  else soil
                rain  = 0.0 if np.isnan(rain)  else rain
                prox  = 0.0 if np.isnan(prox)  else prox

                rain_min   = SPECIES[species_id]["rain_min"]
                is_suitable = (not no_p and not in_p
                               and rain >= rain_min and soil >= 0.3)

                is_reseed  = (self.y, self.x) in self.reseeding_targets

                r, breakdown = self.reward_fn.placement(
                    self.x, self.y, species_id,
                    soil, rain, slope, prox, in_p,
                    is_hover=False,
                    cover_deployed=self.cover_deployed,
                    is_rainy=self.weather.is_rainy(),
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
                    self.reseeding_targets.discard((self.y, self.x))

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
            # Navigate toward base
            dx = np.sign(self.base_x - self.x)
            dy = np.sign(self.base_y - self.y)
            self.x = int(np.clip(self.x + dx, 0, ZONE_SIZE - 1))
            self.y = int(np.clip(self.y + dy, 0, ZONE_SIZE - 1))

            if self.x == self.base_x and self.y == self.base_y:
                self.drone_state = STATE_LANDING
                self.energy.recharge(0.5)
                self.missions_completed += 1
                # Schedule reseeding from monitoring queue
                targets = self.monitor.get_top_targets(3)
                for t in targets:
                    self.reseeding_targets.add((t["y"], t["x"]))
                info["landed"] = True

        # ── Monitoring step ────────────────────────────────────────
        if self.timestep % MONITORING_INTERVAL == 0 and self.timestep > 0:
            rain_map = self.rain_stack[self.season]
            _, gr    = self.growth.step(self.timestep, rain_map)
            _, dr    = self.disturbance.step(self.growth, self.timestep)
            total_r += gr + dr

            # Ingest new failures into monitoring system
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
        return float(self.terrain[:, :, 2].mean())

    def _sanitise(self, arr):
        return np.nan_to_num(arr, nan=0.0, posinf=1.0, neginf=0.0).astype(np.float32)

    def _obs(self) -> Dict:
        half = OBS_WINDOW // 2
        padded = np.pad(self.terrain,
                        ((half,half),(half,half),(0,0)), mode="edge")
        window = padded[self.y:self.y+OBS_WINDOW,
                        self.x:self.x+OBS_WINDOW]

        dist_base = float(np.sqrt((self.x-self.base_x)**2
                                  + (self.y-self.base_y)**2) / (ZONE_SIZE*1.4))

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
        abort_score = float(zone_score < ZONE_MIN_SOIL)
        is_reseed   = float(len(self.reseeding_targets) > 0)

        mission_vec = np.array([
            zone_score, rain_mean, covered_pct,
            failed_n, reseed_n, abort_score,
            self.missions_completed / 10.0,
            is_reseed,
        ], dtype=np.float32)

        lc = self.growth.lifecycle_map()

        return {
            "terrain_window":  self._sanitise(window),
            "drone_vector":    np.clip(drone_vec, 0.0, 1.0),
            "coverage_map":    self._sanitise(self.coverage_map[:,:,np.newaxis]),
            "lifecycle_map":   self._sanitise(lc[:,:,np.newaxis]),
            "disturbance_map": self._sanitise(self.dist_grid[:,:,np.newaxis]),
            "obstacle_map":    self._sanitise(self.obs_grid[:,:,np.newaxis]),
            "mission_vector":  np.clip(mission_vec, 0.0, 1.0),
        }

    def _metrics(self) -> dict:
        seeds = list(self.growth.seeds.values())
        if not seeds:
            return {m: 0.0 for m in EVAL_METRICS}

        n_suit  = int((~self.no_plant).sum()
                      - (self.dist_grid >= 0.9).sum())
        n_seeded = sum(1 for s in seeds if s.is_suitable)
        pct      = n_seeded / max(n_suit, 1)

        counts = np.array(list(self.species_counts.values()), dtype=float)
        counts = counts[counts > 0]
        H = (-np.sum((counts/counts.sum())*np.log(counts/counts.sum()))
             if len(counts) > 1 else 0.0)

        pos = [(s.x, s.y) for s in seeds]
        viol = sum(
            1 for i,(x1,y1) in enumerate(pos)
            for x2,y2 in pos[i+1:]
            if abs(x1-x2)+abs(y1-y2) < MIN_SEED_SPACING
        )

        return {
            "pct_suitable_seeded":   round(pct, 4),
            "mean_soil_score":       round(float(np.mean([s.soil_score for s in seeds])), 4),
            "species_entropy":       round(float(H), 4),
            "spacing_violations":    int(viol),
            "protected_area_seeds":  int(sum(1 for s in seeds if s.in_protected)),
            "seasonal_rain_score":   round(float(np.mean([s.rain_score for s in seeds])), 4),
            "reseeding_count":       self.growth.summary()["reseeding_count"],
            "missions_completed":    self.missions_completed,
            "battery_empty_events":  self.energy.empty_events,
            "obstacles_avoided":     self.obstacles_avoided,
        }

    def render(self, mode="rgb_array"):
        canvas = np.zeros((ZONE_SIZE, ZONE_SIZE, 3), dtype=np.uint8)
        soil   = np.nan_to_num(self.terrain[:,:,2], nan=0.0)
        bg     = (soil * 180).astype(np.uint8)
        canvas[:,:,0] = bg; canvas[:,:,1] = bg; canvas[:,:,2] = bg
        canvas[self.no_plant]              = [80,  80,  80]
        canvas[self.dist_grid > 0.5, 2]   = 200
        canvas[self.obs_grid  > 0.7, 0]   = 200
        lc = self.growth.lifecycle_map()
        for y in range(ZONE_SIZE):
            for x in range(ZONE_SIZE):
                v = lc[y, x]
                if   v == -1.0: canvas[y,x] = [180,40,40]
                elif v > 0:     canvas[y,x] = [20, int(100+v*155), 20]
        canvas[self.y, self.x] = [255, 220, 0]
        return canvas

    def close(self):
        pass
