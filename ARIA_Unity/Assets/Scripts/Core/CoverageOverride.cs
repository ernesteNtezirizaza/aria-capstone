namespace ARIA.Core
{
    /// Scripted reseed-target navigation, mirroring rwanda_env.py's step()
    /// exactly: whenever a reseed target is queued and the drone is in
    /// STATE_SEEDING, the environment itself overrides the policy's chosen
    /// movement direction with a direct line toward the NEAREST queued
    /// target (rwanda_env.py: min(reseeding_targets, key=manhattan_dist)),
    /// only overriding species once the drone actually arrives at the
    /// target cell. Ordinary coverage/navigation (no reseed target queued)
    /// is NOT scripted in rwanda_env.py at all -- the trained policy's own
    /// action drives it entirely. An earlier version of this file also had
    /// a serpentine coverage-sweep pattern for that case; it had no
    /// equivalent in rwanda_env.py, meaning the drone's "normal" seeding
    /// behavior in the demo was a scripted pattern, not the trained
    /// policy's actual navigation decisions. Removed for that reason.
    public static class CoverageOverride
    {
        public static bool Enabled = true;

        // Detects a reseed target the drone can't actually make progress
        // toward (most commonly one that's obstacle-blocked from every
        // approach angle, now that targets queue continuously instead of
        // rarely -- see TryGetOverrideAction below). rwanda_env.py has no
        // equivalent give-up logic (its scripted move still runs the
        // normal obstacle check every step, same as here), but nothing
        // there can get stuck in the first place, since a blocked step
        // there just holds position for one step while the underlying
        // target selection itself never changes -- this is a Unity-only
        // safety valve for the same real-world case of a genuinely
        // unreachable target, not a training-behavior deviation.
        private static (int x, int y) _lastReseedTarget = (-1, -1);
        private static (int x, int y) _lastDronePos = (-1, -1);
        private static int _stuckSteps = 0;
        private const int MAX_STUCK_STEPS = 6;

        public static bool TryGetOverrideAction(EpisodeState s, out int action, out bool suppressSeeding)
        {
            action = 0;
            suppressSeeding = false;
            if (!Enabled) return false;

            if (s.SeedsRemaining <= 0) return false;
            if (s.ReseedingTargets.Count == 0) return false;

            // Nearest queued target by Manhattan distance, matching
            // rwanda_env.py's min(reseeding_targets, key=lambda t: abs(t[0]-y)+abs(t[1]-x))
            // exactly -- not just the first one found in iteration order.
            (int y, int x) target = default;
            int bestDist = int.MaxValue;
            foreach (var t in s.ReseedingTargets)
            {
                int dist = System.Math.Abs(t.y - s.Y) + System.Math.Abs(t.x - s.X);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    target = t;
                }
            }

            var dronePos = (s.X, s.Y);
            _stuckSteps = (target == _lastReseedTarget && dronePos == _lastDronePos) ? _stuckSteps + 1 : 0;
            _lastReseedTarget = target;
            _lastDronePos = dronePos;

            if (_stuckSteps >= MAX_STUCK_STEPS)
            {
                s.ReseedingTargets.Remove(target);
                s.ReseedSpeciesMap.Remove(target);
                _stuckSteps = 0;
                _lastReseedTarget = (-1, -1);
                return false; // let the trained policy's own action apply this step instead
            }

            int? recommended = s.ReseedSpeciesMap.TryGetValue(target, out int rec) ? rec : (int?)null;
            return TryStepToward(s, target.x, target.y, out action, out suppressSeeding, recommended);
        }

        private static bool TryStepToward(EpisodeState s, int tx, int ty, out int action, out bool suppressSeeding, int? forcedSpecies = null)
        {
            action = 0;
            suppressSeeding = false;

            int dxTotal = tx - s.X;
            int dyTotal = ty - s.Y;
            int chebyshev = System.Math.Max(System.Math.Abs(dxTotal), System.Math.Abs(dyTotal));

            int dx = System.Math.Sign(dxTotal);
            int dy = System.Math.Sign(dyTotal);
            if (dx == 0 && dy == 0)
            {
                return false;
            }

            suppressSeeding = chebyshev > 1; // this move won't land exactly on the target yet

            int dirIdx = DirIndexFor(dy, dx);
            // Reseed targets carry MonitoringSystem's better-suited species recommendation.
            int speciesId = forcedSpecies ?? BestSpeciesFor(s.Zone, tx, ty);
            action = dirIdx * ARIAConstants.N_SPECIES + speciesId;
            return true;
        }

        private static int DirIndexFor(int dy, int dx)
        {
            for (int i = 0; i < ARIAConstants.DIRECTIONS.Length; i++)
                if (ARIAConstants.DIRECTIONS[i].dy == dy && ARIAConstants.DIRECTIONS[i].dx == dx)
                    return i;
            return 0; // dy/dx are always in {-1,0,1} from Math.Sign, so this never actually falls through
        }

        private static int BestSpeciesFor(ZoneData zone, int x, int y)
        {
            float rain = zone.Terrain[y, x, 3];
            if (float.IsNaN(rain)) rain = 0f;

            int count = 0;
            for (int i = 0; i < ARIAConstants.N_SPECIES; i++)
                if (rain >= ARIAConstants.SPECIES_RAIN_MIN[i]) count++;
            if (count == 0) return 0;

            int target = (x * 7 + y * 13) % count;
            int seen = 0;
            for (int i = 0; i < ARIAConstants.N_SPECIES; i++)
            {
                if (rain >= ARIAConstants.SPECIES_RAIN_MIN[i])
                {
                    if (seen == target) return i;
                    seen++;
                }
            }
            return 0;
        }

        public static void Reset()
        {
            _lastReseedTarget = (-1, -1);
            _lastDronePos = (-1, -1);
            _stuckSteps = 0;
        }
    }
}
