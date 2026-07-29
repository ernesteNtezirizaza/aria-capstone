using System;
using UnityEngine;
using ARIA.Core;
using ARIA.Systems;

namespace ARIA.Core
{
    public struct StepResult
    {
        public bool Terminated;
        public bool Truncated;
        public bool SeedDropped;
        public bool IsSuitable;
        public bool EmergencyLand;
        public bool ValidAbort;
        public bool BadAbort;
        public bool ObstacleHit;
        public bool ObstacleCleared;
        public bool Landed;
        public bool ReturningBattery;

        public bool BatteryDepleted;

        public bool MissionComplete;

        /* Placement was attempted but refused -- mirrors rwanda_env.py's
           w_redundant_penalty (already-covered cell, not a reseed) and the
           reward function's slope penalty (cell too steep to plant, from
           the same real no_plant mask used for is_suitable). */
        public bool RedundantPlacementBlocked;
        public bool TooSteepBlocked;

        /* Real per-step reward, mirroring rwanda_env.py's step() total_r
           exactly (see ActionDispatcher.Step() for the full breakdown). */
        public float Reward;
    }

    public static class ActionDispatcher
    {
        /* The full per-timestep transition, mirroring rwanda_env.py's
           step() exactly: advances weather/energy, dispatches the chosen
           action (move+seed, hover, abort, cover, altitude, or emergency),
           scores it via the same multi-tier reward formula as
           reward_function.py, and updates drone/mission state. Growth,
           disturbance, and reseed-queue updates happen in DroneController,
           which calls this once per simulated step. */
        public static StepResult Step(EpisodeState s, int action, System.Random rng)
        {
            var result = new StepResult();

            float realRain = s.Zone.Terrain[s.Y, s.X, 3];
            float rainVal = DemoConditions.GetEffectiveRainfall(realRain, s.Timestep);
            s.Weather.Step(rainVal, s.Timestep);

            /* Mirrors energy_system.py's step(steps_to_base) exactly -- see
               EnergySystem.Step for why a fixed return threshold was a real
               bug, not just a simplification. */
            int stepsToBase = Mathf.Max(Mathf.Abs(s.BaseX - s.X), Mathf.Abs(s.BaseY - s.Y));
            var energyInfo = s.Energy.Step(s.Weather, stepsToBase);
            s.Season = s.Weather.CurrentSeason;

            /* Mirrors rwanda_env.py's step() exactly: "if action == EMERGENCY
               or energy_info['is_critical']" terminates immediately, wherever
               the drone happens to be -- there is no "try to fly home first"
               grace period in the real trained environment. The distance-aware
               ShouldReturn threshold above is what's actually supposed to get
               the drone back to base with margin to spare; IsCritical is the
               hard safety cutoff for when that didn't happen in time, not a
               normal end state reached via a scripted return flight. (An
               earlier version of this code held the battery steady and let a
               "critical return" fly all the way home before terminating, and
               cancelled the whole thing if the weather turned sunny mid-flight
               -- neither of those exist in rwanda_env.py; the battery just
               keeps draining normally every step, exactly as it does here now.) */
            if (action == ARIAConstants.EMERGENCY || energyInfo.IsCritical)
            {
                result.EmergencyLand = true;
                result.Terminated = true;
                result.BatteryDepleted = true;
                s.MissionCompleteReturning = false;
                /* Mirrors rwanda_env.py exactly: a voluntary EMERGENCY while
                   not actually critical pays nothing (punishment is the lost
                   future seeding reward); genuine battery death pays
                   battery_empty. Either way this returns before the
                   universal step penalty below -- matching Python, which
                   also returns here without ever reaching its own. */
                result.Reward = (action == ARIAConstants.EMERGENCY && !energyInfo.IsCritical)
                    ? 0f
                    : ARIAConstants.REWARD_BATTERY_EMPTY;
                s.Timestep++;
                return result;
            }

            if (action == ARIAConstants.ABORT_ACTION)
            {
                float zoneScore = s.ZoneSuitability();
                if (zoneScore < ARIAConstants.ZONE_MIN_SUITABILITY)
                {
                    result.ValidAbort = true;
                    /* Mirrors rwanda_env.py's valid_abort_rewarded: pays out
                       only the first time per episode. */
                    if (!s.ValidAbortRewarded)
                    {
                        result.Reward += ARIAConstants.REWARD_BATTERY_SAVE;
                        s.ValidAbortRewarded = true;
                    }
                }
                else
                {
                    result.BadAbort = true;
                    result.Reward += ARIAConstants.REWARD_BAD_ABORT_ACTUAL;
                }
                s.DroneState = ARIAConstants.STATE_RETURNING;
                s.AbortTriggered = true;
            }
            else if (action == ARIAConstants.COVER_DEPLOY)
            {
                s.CoverDeployed = true;
                result.Reward += s.Weather.IsRainy() ? ARIAConstants.REWARD_COVER_CORRECT : ARIAConstants.REWARD_COVER_WRONG;
            }
            else if (action == ARIAConstants.COVER_RETRACT)
            {
                s.CoverDeployed = false;
                result.Reward += s.Weather.IsSunny() ? ARIAConstants.REWARD_COVER_CORRECT : ARIAConstants.REWARD_COVER_WRONG;
            }
            else if (action == ARIAConstants.ALT_UP)
            {
                bool wasBlocked = s.Altitude < 0.5f &&
                    s.Zone.ObsGrid[s.Y, s.X] >= ARIAConstants.OBSTACLE_THRESHOLD;
                s.Altitude = Mathf.Min(1.0f, s.Altitude + 0.1f);
                if (wasBlocked)
                {
                    s.ObstaclesAvoided++;
                    s.DroneState = ARIAConstants.STATE_SEEDING;
                    result.ObstacleCleared = true;
                    result.Reward += ARIAConstants.REWARD_OBSTACLE_CLEAR;
                }
                /* else: no reward for unnecessary altitude increase, matching rwanda_env.py. */
            }
            else if (action == ARIAConstants.ALT_DOWN)
            {
                /* Unity-only demo-realism decision, not a training-parity
                   claim: only actually let the drone descend toward low
                   altitude once it's near base -- the flat, empty "grey"
                   ground surrounding the planted zone -- not out over the
                   working zone itself, where low altitude reads as flying
                   dangerously close to canopy/slope for no reason. Reuses
                   the same RETURN_DESCENT_RANGE the return-flight cruise-
                   then-descend logic further down already treats as "close
                   enough to base to be safe to descend" -- previously this
                   gate keyed off IsGreyTerrain (steep/no-plant ground
                   *inside* the zone), which was backwards: that's exactly
                   the ground the drone is never supposed to be low over.
                   The action still "fires" even when it's a no-op here,
                   same as ALT_UP's own no-reward no-op case above. */
                bool nearBase = IsNearBase(s);
                if (nearBase)
                {
                    s.Altitude = Mathf.Max(0.0f, s.Altitude - 0.1f);
                }
            }
            else if (action == ARIAConstants.HOVER_ACTION)
            {
                /* Mirrors rwanda_env.py's hover_penalty() exactly: this is IN
                   ADDITION to the universal step penalty applied at the end
                   of Step(), so hovering costs step_penalty twice over --
                   a real quirk of the trained reward, not a bug to "fix" here. */
                result.Reward += -ARIAConstants.REWARD_STEP_PENALTY;
            }
            else if (s.DroneState != ARIAConstants.STATE_RETURNING &&
                     s.DroneState != ARIAConstants.STATE_LANDING)
            {
                int dirIdx    = action / ARIAConstants.N_SPECIES;
                int speciesId = action % ARIAConstants.N_SPECIES;
                var (dy, dx)  = ARIAConstants.DIRECTIONS[dirIdx];

                int newX = Mathf.Clamp(s.X + dx, 0, ARIAConstants.ZONE_SIZE - 1);
                int newY = Mathf.Clamp(s.Y + dy, 0, ARIAConstants.ZONE_SIZE - 1);

                /* Matches env/rwanda_env.py's step() exactly: obstacles are real, static
                   terrain features (from compute_obstacle() in preprocess.py) that always
                   block low-altitude flight into them, unconditionally -- not something a
                   demo toggle turns on. Blocking simply holds the drone at its current
                   cell for this step (rwanda_env.py never rescues the move with an
                   automatic reroute search) -- it's on the trained policy to pick a
                   different direction, or climb via ALT_UP, on a later step, exactly as
                   it actually learned to. An earlier version of this searched adjacent
                   directions (CW/CCW/reverse) and auto-relocated the drone within the
                   same step; that was never something the policy was trained under. */
                bool obstacleAtDestination = s.Zone.ObsGrid[newY, newX] > ARIAConstants.OBSTACLE_THRESHOLD;
                bool blocked = obstacleAtDestination && s.Altitude < ARIAConstants.OBSTACLE_SAFE_ALTITUDE;

                if (blocked)
                {
                    result.ObstacleHit = true;
                    result.Reward += ARIAConstants.REWARD_OBSTACLE_HIT;
                    s.DroneState = ARIAConstants.STATE_OBSTACLE;
                }
                else
                {
                    s.X = newX;
                    s.Y = newY;
                }

                bool alreadyPlanted = s.CoverageMap[s.Y, s.X] >= 1.0f;
                bool isReseed        = s.ReseedingTargets.Contains((s.Y, s.X));
                bool noPlant         = s.Zone.NoPlant[s.Y, s.X];

                /* Mirrors rwanda_env.py's step() exactly: a reseed attempt is
                   a deliberate correction, not accidental redundancy, so it
                   bypasses the already-planted block -- without this
                   exception a reseed target could never actually be
                   replanted even after being successfully reached, since its
                   cell was already marked covered by the original (failed)
                   seed. The queue could then only ever shrink by timing out,
                   never by succeeding. */
                bool blockedRedundant = alreadyPlanted && !isReseed;
                /* Too-steep-to-plant is hard-blocked regardless of reseed
                   status, matching how no_plant feeds is_suitable identically
                   for both cases in reward_function.py -- there is no reseed
                   exception for slope there either. IsGreyTerrain widens this
                   beyond the baked no_plant mask to also cover cells above
                   MAX_SLOPE_DEG -- a Unity-only demo-realism decision (see
                   its own comment), not a training-parity claim: rwanda_env.py
                   never hard-blocks on slope alone, only via no_plant. */
                bool blockedSlope = IsGreyTerrain(s, s.Y, s.X);

                if (s.DroneState == ARIAConstants.STATE_SEEDING && s.SeedsRemaining > 0
                    && !blockedRedundant && !blockedSlope)
                {
                    float soil  = s.Zone.SoilAt(s.Y, s.X);
                    float rain  = s.Zone.Terrain[s.Y, s.X, 3];
                    float slope = s.Zone.SlopeAt(s.Y, s.X) * 90f;
                    float prox  = s.Zone.DistGrid[s.Y, s.X];
                    bool inProtected = prox >= ARIAConstants.PROTECTED_PROXIMITY_THRESHOLD;

                    soil = float.IsNaN(soil) ? 0f : soil;
                    rain = float.IsNaN(rain) ? 0f : rain;
                    prox = float.IsNaN(prox) ? 0f : prox;

                    float rainMin = ARIAConstants.SPECIES_RAIN_MIN[speciesId];
                    bool isSuitable = !noPlant && !inProtected
                        && rain >= rainMin && soil >= ARIAConstants.ZONE_MIN_SOIL;

                    /* Coverage bonus/penalty -- skipped entirely for a genuine
                       reseed, since revisiting a known failure is deliberate
                       correction, not redundancy (mirrors rwanda_env.py). Note:
                       "already_covered" can't actually happen here in Unity,
                       since blockedRedundant already excludes alreadyPlanted
                       && !isReseed from reaching this branch -- kept for
                       structural fidelity to rwanda_env.py regardless. */
                    if (!isReseed)
                    {
                        if (alreadyPlanted) result.Reward += ARIAConstants.REWARD_W_REDUNDANT_PENALTY;
                        else if (isSuitable) result.Reward += ARIAConstants.REWARD_W_NEW_COVERAGE_BONUS;
                    }

                    /* Tier 1 placement reward -- mirrors reward_function.py's placement(). */
                    float rainOk = Mathf.Max(0f, rain - rainMin) / (1f - rainMin + 1e-6f);
                    float slopePen = Mathf.Min(slope / ARIAConstants.MAX_SLOPE_DEG, 1f);

                    /* Spacing/cluster check against every seed ever dropped
                       this episode (Python's self.seeded set is never pruned,
                       so reusing Growth.Seeds -- unpruned itself -- matches
                       exactly), skipped for a genuine reseed. */
                    float cluster = 0f;
                    if (!isReseed)
                    {
                        foreach (var existingSeed in s.Growth.Seeds.Values)
                        {
                            int manhattan = Mathf.Abs(existingSeed.X - s.X) + Mathf.Abs(existingSeed.Y - s.Y);
                            if (manhattan < ARIAConstants.MIN_SEED_SPACING)
                            {
                                cluster = -ARIAConstants.REWARD_W_SPACING;
                                break;
                            }
                        }
                    }

                    float protectedPen = inProtected ? -ARIAConstants.REWARD_W_PROTECTED : 0f;
                    float distPen = -ARIAConstants.REWARD_W_DISTURBANCE * prox;
                    float reseedBonus = isReseed ? ARIAConstants.REWARD_W_RESEED : 0f;

                    bool isRainy = s.Weather.IsRainy();
                    float coverR;
                    if (isRainy && s.CoverDeployed) coverR = ARIAConstants.REWARD_COVER_CORRECT;
                    else if (isRainy && !s.CoverDeployed) coverR = ARIAConstants.REWARD_COVER_WRONG;
                    else if (!isRainy && s.CoverDeployed) coverR = ARIAConstants.REWARD_COVER_WRONG;
                    else coverR = 0f;

                    /* Diversity entropy needs this species' count incremented
                       first, matching reward_function.py's placement() order
                       (species_counts[species_id] += 1 before _diversity()). */
                    s.SpeciesCounts[speciesId]++;
                    float diversityR = SpeciesDiversityReward(s.SpeciesCounts);

                    float suitableBonus = isSuitable ? ARIAConstants.REWARD_W_SUITABLE_BONUS : 0f;

                    result.Reward +=
                        ARIAConstants.ZONE_SUIT_W_SOIL * soil
                        + ARIAConstants.ZONE_SUIT_W_RAIN * rainOk
                        - ARIAConstants.ZONE_SUIT_W_SLOPE * slopePen
                        + cluster + protectedPen + distPen + reseedBonus + coverR + diversityR + suitableBonus;

                    s.Growth.Register(speciesId, s.X, s.Y, s.Timestep,
                        soil, rain, slope, prox, isSuitable, inProtected);

                    s.CoverageMap[s.Y, s.X] = 1.0f;
                    if (!noPlant) s.CoveredPlantableCells++;
                    s.SeedsRemaining -= 1;

                    if (isReseed)
                    {
                        s.Monitor.MarkReseeded(s.X, s.Y);
                        s.ReseedingTargets.Remove((s.Y, s.X));
                        s.ReseedSpeciesMap.Remove((s.Y, s.X));
                    }

                    result.SeedDropped = true;
                    result.IsSuitable = isSuitable;
                }
                else if (blockedRedundant)
                {
                    result.RedundantPlacementBlocked = true;
                    /* Unlike rwanda_env.py (where a redundant placement still
                       drops a real, duplicate seed and only adds this as a
                       penalty), Unity blocks the drop entirely here -- no seed
                       is consumed or registered. Applying just the penalty
                       term is the closest faithful value for what Unity's
                       simulation actually does at this branch, not a claim
                       that the full tier-1 formula also ran. */
                    result.Reward += ARIAConstants.REWARD_W_REDUNDANT_PENALTY;
                }
                else if (blockedSlope)
                {
                    result.TooSteepBlocked = true;
                    /* Unlike rwanda_env.py (where a no_plant cell still runs
                       the full placement formula, just with is_suitable=false
                       dragging is_suitable-dependent terms to zero), Unity
                       blocks the drop entirely -- no seed consumed. Applying
                       the slope penalty at its ceiling (w_slope * 1.0) is the
                       same stand-in this branch already used before reward
                       parity, not a claim the full formula ran. */
                    result.Reward += -ARIAConstants.ZONE_SUIT_W_SLOPE;
                }
            }

            s.CoverDeployed = s.Weather.IsRainy();

            bool activelySeeding = s.DroneState == ARIAConstants.STATE_SEEDING
                                 || s.DroneState == ARIAConstants.STATE_NAVIGATING;
            bool seedsExhausted = s.SeedsRemaining <= 0;
            bool fullyPlanted = s.PlantableCells > 0 && s.CoveredPlantableCells >= s.PlantableCells;
            if ((seedsExhausted || fullyPlanted) && activelySeeding && !s.MissionCompleteReturning)
            {
                s.DroneState = ARIAConstants.STATE_RETURNING;
                s.MissionCompleteReturning = true;
                s.ReseedingTargets.Clear();
                s.ReseedSpeciesMap.Clear();
            }

            /* Mirrors rwanda_env.py's should_return check exactly -- no
               weather condition on it there. (An earlier version of this
               code only triggered a battery return in rain, since sunny
               weather was treated as "recharging enough to not need it";
               that's not how the real threshold works -- see
               EnergySystem.Step's distance-aware ShouldReturn.) */
            if (energyInfo.ShouldReturn && activelySeeding)
            {
                s.DroneState = ARIAConstants.STATE_RETURNING;
                result.ReturningBattery = true;
                result.Reward += ARIAConstants.REWARD_BATTERY_SAVE;
            }

            if (s.DroneState == ARIAConstants.STATE_RETURNING)
            {
                int dx = (int)Mathf.Sign(s.BaseX - s.X);
                int dy = (int)Mathf.Sign(s.BaseY - s.Y);
                s.X = Mathf.Clamp(s.X + dx, 0, ARIAConstants.ZONE_SIZE - 1);
                s.Y = Mathf.Clamp(s.Y + dy, 0, ARIAConstants.ZONE_SIZE - 1);

                /* Cruise above canopy until clear of the planted zone, then
                   descend -- genuinely checked against real tree positions
                   now, not just assumed from distance-to-base. A tall
                   Seedling/Mature tree sitting inside the final descent
                   radius used to get flown straight through, since the old
                   curve only looked at distance, never at what was actually
                   underneath. */
                int distToBase = Mathf.Max(Mathf.Abs(s.BaseX - s.X), Mathf.Abs(s.BaseY - s.Y));
                bool overCanopy = HasTreeCanopyAt(s, s.X, s.Y);
                s.Altitude = (!overCanopy && distToBase <= ARIAConstants.RETURN_DESCENT_RANGE)
                    ? Mathf.Clamp01((float)distToBase / ARIAConstants.RETURN_DESCENT_RANGE)
                    : 1f;

                if (s.X == s.BaseX && s.Y == s.BaseY)
                {
                    s.DroneState = ARIAConstants.STATE_LANDING;
                    s.MissionsCompleted++;
                    result.Landed = true;

                    /* Critical-battery termination now fires immediately at
                       the top of Step() (matching rwanda_env.py), the moment
                       energyInfo.IsCritical goes true -- not after a
                       completed return flight -- so a landing here can only
                       be a voluntary/battery-driven return that made it back
                       safely, or the mission-complete flight. */
                    if (s.MissionCompleteReturning)
                    {
                        s.Energy.Recharge(0.5f);
                        result.MissionComplete = true;
                        result.Terminated = true;
                        s.MissionCompleteReturning = false;
                    }
                    else
                    {
                        s.DroneState = ARIAConstants.STATE_SEEDING;
                    }
                }
            }

            /* Growth/disturbance themselves still only tick once per
               MONITORING_INTERVAL steps, preserving how often a seed
               actually gets a mortality/disturbance roll -- running that
               part every step would make animal disturbance kill far more
               aggressively, not just faster to notice. */
            const int MONITORING_INTERVAL = 10;
            if (s.Timestep % MONITORING_INTERVAL == 0 && s.Timestep > 0)
            {
                float[,] rainMap = ExtractChannel(s.Zone, 3);
                var (maturedPositions, growthReward) = s.Growth.Step(s.Timestep, rainMap);
                result.Reward += growthReward;
                if (DemoConditions.AnimalDisturbanceEnabled)
                    result.Reward += s.Disturbance.Step(s.Growth, s.Timestep);

                /* Close the reseed feedback loop: any seed that matured this
                   step, at a position that was a pending reseed, is a real
                   success outcome for whichever species SpeciesRecommender
                   picked there -- feed it back before ingesting new failures. */
                s.Monitor.ResolveMatured(maturedPositions);
            }

            /* Ingesting failures and queueing reseed targets runs every
               step, not gated to MONITORING_INTERVAL -- a real-world goat
               eating a seed should show up as queued for reseeding
               immediately, not after up to 10 steps (1.5s) of batching
               delay. Growth.FailedCells simply accumulates between the
               (still gated) growth/disturbance ticks above, and drains
               here every step, so this is a no-op most steps and only
               does real work on the step a failure actually happened. */
            if (s.Growth.FailedCells.Count > 0)
            {
                s.Monitor.IngestFailures(new System.Collections.Generic.List<FailedCell>(s.Growth.FailedCells));
                s.Growth.FailedCells.Clear();
            }

            /* Queue reseed targets continuously as failures come in, rather
               than only once per full return-to-base cycle. A cell already
               queued (not yet visited) simply gets its entry refreshed, not
               duplicated, since ReseedingTargets is a HashSet and
               ReseedSpeciesMap is keyed by (y, x). Mirrors rwanda_env.py. */
            foreach (var t in s.Monitor.GetTopTargets(3))
            {
                s.ReseedingTargets.Add((t.Y, t.X));
                s.ReseedSpeciesMap[(t.Y, t.X)] = t.RecommendedSpecies;
            }

            s.Timestep++;

            /* MAX_STEPS is the episode-length bound the policy was trained
               under in rwanda_env.py, but that's a training-time sample-
               efficiency cap, not a promise that the zone actually finishes
               getting seeded. Normal completion already happens above via
               MissionComplete (seed budget fully placed / nowhere left to
               plant) or BatteryDepleted -- truncating on step count alone
               would let the live demo give up and reset a zone while seeds
               are still sitting unused in the hopper, which is not
               acceptable for the demo: it should always finish seeding
               before it stops. This is now purely a safety valve against a
               genuinely stranded last seed (e.g. a target no path can ever
               reach), not a real end state. */
            result.Truncated = s.Timestep >= ARIAConstants.MAX_STEPS * 4;

            /* Universal per-step penalty -- mirrors rwanda_env.py's step()
               exactly: applied every non-early-return step regardless of
               action (including on top of hover's own extra penalty above),
               but never on the EmergencyLand path, which returns before this
               point, exactly as Python's does too. */
            result.Reward += -ARIAConstants.REWARD_STEP_PENALTY;

            return result;
        }

        /* Mirrors reward_function.py's _diversity(): Shannon entropy of
           species placement counts so far this episode, normalised by
           ln(N_SPECIES) so it stays in a comparable range regardless of
           species count. */
        private static float SpeciesDiversityReward(System.Collections.Generic.Dictionary<int, int> speciesCounts)
        {
            int total = 0;
            foreach (var c in speciesCounts.Values) total += c;
            if (total == 0) return 0f;

            float h = 0f;
            foreach (var c in speciesCounts.Values)
            {
                if (c <= 0) continue;
                float p = (float)c / total;
                h += -p * Mathf.Log(p);
            }
            return ARIAConstants.REWARD_W_DIVERSITY * h / Mathf.Log(ARIAConstants.N_SPECIES);
        }

        private static float[,] ExtractChannel(ZoneData zone, int channel)
        {
            int size = ARIAConstants.ZONE_SIZE;
            var map = new float[size, size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    map[y, x] = zone.Terrain[y, x, channel];
            return map;
        }

        /* Only Seedling/Mature have a real canopy tall enough to visually
           clip through during a low return-flight pass -- Dropped/
           Germinating are still just a sprout marker at ground level. */
        private static bool HasTreeCanopyAt(EpisodeState s, int x, int y)
        {
            foreach (var seed in s.Growth.Seeds.Values)
            {
                if (seed.X == x && seed.Y == y &&
                    (seed.Stage == SeedStage.Seedling || seed.Stage == SeedStage.Mature))
                    return true;
            }
            return false;
        }

        /* Ground the drone is never allowed to plant on: the baked no_plant
           mask widened to also cover cells whose slope exceeds
           MAX_SLOPE_DEG. Coordinates follow this file's existing [y, x]
           convention (see s.Zone.NoPlant[s.Y, s.X] / SlopeAt(s.Y, s.X) above). */
        private static bool IsGreyTerrain(EpisodeState s, int y, int x)
        {
            bool noPlant = s.Zone.NoPlant[y, x];
            float slopeDeg = s.Zone.SlopeAt(y, x) * 90f;
            return noPlant || slopeDeg >= ARIAConstants.MAX_SLOPE_DEG;
        }

        /* Close enough to base to be over the flat, empty "grey" ground
           surrounding the planted zone, rather than out over the working
           zone itself -- the same RETURN_DESCENT_RANGE threshold the
           return-flight cruise-then-descend logic uses for exactly this
           judgment. */
        private static bool IsNearBase(EpisodeState s)
        {
            int distToBase = Mathf.Max(Mathf.Abs(s.BaseX - s.X), Mathf.Abs(s.BaseY - s.Y));
            return distToBase <= ARIAConstants.RETURN_DESCENT_RANGE;
        }
    }
}
