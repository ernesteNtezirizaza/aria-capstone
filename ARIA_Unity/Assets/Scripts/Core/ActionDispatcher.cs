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
    }

    public static class ActionDispatcher
    {
        public static StepResult Step(EpisodeState s, int action, System.Random rng)
        {
            var result = new StepResult();

            float realRain = s.Zone.Terrain[s.Y, s.X, 3];
            float rainVal = DemoConditions.GetEffectiveRainfall(realRain, s.Timestep);
            s.Weather.Step(rainVal, s.Timestep);
            float batteryBeforeStep = s.Energy.Battery;
            var energyInfo = s.Energy.Step(s.Weather);
            s.Season = s.Weather.CurrentSeason;

            if (s.BatteryCriticalReturning)
            {
                s.Energy.SetBattery(batteryBeforeStep);
            }

            if (action == ARIAConstants.EMERGENCY)
            {
                result.EmergencyLand = true;
                result.Terminated = true;
                result.BatteryDepleted = true;
                s.MissionCompleteReturning = false;
                s.BatteryCriticalReturning = false;
                s.Timestep++;
                return result;
            }

            // ── ABORT_ACTION ──────────────────────────────────────────
            if (action == ARIAConstants.ABORT_ACTION)
            {
                float zoneScore = s.ZoneSuitability();
                if (zoneScore < ARIAConstants.ZONE_MIN_SOIL)
                    result.ValidAbort = true;
                else
                    result.BadAbort = true;
                s.DroneState = ARIAConstants.STATE_RETURNING;
                s.AbortTriggered = true;
            }
            else if (action == ARIAConstants.COVER_DEPLOY)
            {
                s.CoverDeployed = true;
            }
            else if (action == ARIAConstants.COVER_RETRACT)
            {
                s.CoverDeployed = false;
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
                }
            }
            else if (action == ARIAConstants.ALT_DOWN)
            {
                s.Altitude = Mathf.Max(0.0f, s.Altitude - 0.1f);
            }
            else if (action == ARIAConstants.HOVER_ACTION)
            {
                
            }
            else if (s.DroneState != ARIAConstants.STATE_RETURNING &&
                     s.DroneState != ARIAConstants.STATE_LANDING)
            {
                int dirIdx    = action / ARIAConstants.N_SPECIES;
                int speciesId = action % ARIAConstants.N_SPECIES;
                var (dy, dx)  = ARIAConstants.DIRECTIONS[dirIdx];

                int newX = Mathf.Clamp(s.X + dx, 0, ARIAConstants.ZONE_SIZE - 1);
                int newY = Mathf.Clamp(s.Y + dy, 0, ARIAConstants.ZONE_SIZE - 1);

                bool obstacleAtDestination = s.Zone.ObsGrid[newY, newX] > ARIAConstants.OBSTACLE_THRESHOLD;
                bool altitudeMattersHere = !DemoConditions.ObstacleOverlayEnabled;
                bool blocked = obstacleAtDestination &&
                    (!altitudeMattersHere || s.Altitude < ARIAConstants.OBSTACLE_SAFE_ALTITUDE);

                bool wasTransitHop = s.DroneState == ARIAConstants.STATE_NAVIGATING;

                if (blocked)
                {
                    result.ObstacleHit = true;
                    s.DroneState = ARIAConstants.STATE_OBSTACLE;

                    if (DemoConditions.ObstacleOverlayEnabled)
                    {
                        int cwIdx  = FindDirectionIndex(dx, -dy);   // rotate blocked heading 90 deg clockwise
                        int ccwIdx = FindDirectionIndex(-dx, dy);   // rotate blocked heading 90 deg counter-clockwise
                        int revIdx = FindDirectionIndex(-dy, -dx);  // full reverse -- tried last

                        Span<int> tryOrder = stackalloc int[8];
                        int n = 0;
                        if (cwIdx  >= 0) tryOrder[n++] = cwIdx;
                        if (ccwIdx >= 0) tryOrder[n++] = ccwIdx;
                        for (int i = 0; i < ARIAConstants.DIRECTIONS.Length; i++)
                            if (i != cwIdx && i != ccwIdx && i != revIdx) tryOrder[n++] = i;
                        if (revIdx >= 0) tryOrder[n++] = revIdx;

                        for (int k = 0; k < n; k++)
                        {
                            var (ty, tx) = ARIAConstants.DIRECTIONS[tryOrder[k]];
                            int altX = Mathf.Clamp(s.X + tx, 0, ARIAConstants.ZONE_SIZE - 1);
                            int altY = Mathf.Clamp(s.Y + ty, 0, ARIAConstants.ZONE_SIZE - 1);
                            bool altBlocked = s.Zone.ObsGrid[altY, altX] > ARIAConstants.OBSTACLE_THRESHOLD;
                            if (!altBlocked && (altX != s.X || altY != s.Y))
                            {
                                s.X = altX;
                                s.Y = altY;
                                result.ObstacleCleared = true;
                                s.DroneState = wasTransitHop
                                    ? ARIAConstants.STATE_NAVIGATING
                                    : ARIAConstants.STATE_SEEDING;
                                break;
                            }
                        }
                    }
                }
                else
                {
                    s.X = newX;
                    s.Y = newY;
                }

                bool alreadyPlanted = s.CoverageMap[s.Y, s.X] >= 1.0f;
                if (s.DroneState == ARIAConstants.STATE_SEEDING && s.SeedsRemaining > 0 && !alreadyPlanted)
                {
                    float soil  = s.Zone.SoilAt(s.Y, s.X);
                    float rain  = s.Zone.Terrain[s.Y, s.X, 3];
                    float slope = s.Zone.SlopeAt(s.Y, s.X) * 90f;
                    float prox  = s.Zone.DistGrid[s.Y, s.X];
                    bool inProtected = prox >= ARIAConstants.PROTECTED_PROXIMITY_THRESHOLD;
                    bool noPlant     = s.Zone.NoPlant[s.Y, s.X];

                    soil = float.IsNaN(soil) ? 0f : soil;
                    rain = float.IsNaN(rain) ? 0f : rain;
                    prox = float.IsNaN(prox) ? 0f : prox;

                    float rainMin = ARIAConstants.SPECIES_RAIN_MIN[speciesId];
                    bool isSuitable = !noPlant && !inProtected
                        && rain >= rainMin && soil >= ARIAConstants.ZONE_MIN_SOIL;

                    bool isReseed = s.ReseedingTargets.Contains((s.Y, s.X));

                    s.Growth.Register(speciesId, s.X, s.Y, s.Timestep,
                        soil, rain, slope, prox, isSuitable, inProtected);

                    s.CoverageMap[s.Y, s.X] = 1.0f;
                    if (!noPlant) s.CoveredPlantableCells++;
                    s.SpeciesCounts[speciesId]++;
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

            if (energyInfo.ShouldReturn && activelySeeding)
            {
                s.DroneState = ARIAConstants.STATE_RETURNING;
                s.BatteryCriticalReturning = true;
                result.ReturningBattery = true;
            }

            if (s.DroneState == ARIAConstants.STATE_RETURNING)
            {
                int dx = (int)Mathf.Sign(s.BaseX - s.X);
                int dy = (int)Mathf.Sign(s.BaseY - s.Y);
                s.X = Mathf.Clamp(s.X + dx, 0, ARIAConstants.ZONE_SIZE - 1);
                s.Y = Mathf.Clamp(s.Y + dy, 0, ARIAConstants.ZONE_SIZE - 1);

                if (s.X == s.BaseX && s.Y == s.BaseY)
                {
                    s.DroneState = ARIAConstants.STATE_LANDING;
                    s.MissionsCompleted++;

                    // Pull top 3 reseeding targets into the active queue, carrying
                    // the recommended replacement species along with each cell.
                    var targets = s.Monitor.GetTopTargets(3);
                    foreach (var t in targets)
                    {
                        s.ReseedingTargets.Add((t.Y, t.X));
                        s.ReseedSpeciesMap[(t.Y, t.X)] = t.RecommendedSpecies;
                    }

                    result.Landed = true;

                    if (s.MissionCompleteReturning)
                    {
                        s.Energy.Recharge(0.5f);
                        result.MissionComplete = true;
                        result.Terminated = true;
                        s.MissionCompleteReturning = false;
                    }
                    else if (s.BatteryCriticalReturning)
                    {
                        // Comes to rest exactly empty, as if it just barely made it back.
                        s.Energy.SetBattery(0f);
                        result.BatteryDepleted = true;
                        result.Terminated = true;
                        s.BatteryCriticalReturning = false;
                    }
                    else
                    {
                        s.DroneState = ARIAConstants.STATE_SEEDING;
                    }
                }
            }

            const int MONITORING_INTERVAL = 10;
            if (s.Timestep % MONITORING_INTERVAL == 0 && s.Timestep > 0)
            {
                float[,] rainMap = ExtractChannel(s.Zone, 3);
                s.Growth.Step(s.Timestep, rainMap);
                if (DemoConditions.AnimalDisturbanceEnabled)
                    s.Disturbance.Step(s.Growth, s.Timestep);

                s.Monitor.IngestFailures(new System.Collections.Generic.List<FailedCell>(s.Growth.FailedCells));
                s.Growth.FailedCells.Clear();
            }

            // ── Timestep advance + termination check ──────────────────
            s.Timestep++;
            result.Truncated = s.Timestep >= ARIAConstants.MAX_STEPS;

            return result;
        }

        private static int FindDirectionIndex(int dy, int dx)
        {
            for (int i = 0; i < ARIAConstants.DIRECTIONS.Length; i++)
                if (ARIAConstants.DIRECTIONS[i].dy == dy && ARIAConstants.DIRECTIONS[i].dx == dx)
                    return i;
            return -1;
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
    }
}
