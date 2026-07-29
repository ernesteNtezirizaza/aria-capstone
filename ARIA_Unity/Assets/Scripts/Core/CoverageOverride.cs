using System.Collections.Generic;

namespace ARIA.Core
{
    /// Scripted navigation override for the live demo. Two layers, reseed
    /// targets take priority over the coverage sweep:
    ///
    /// 1. Reseed targets: mirrors rwanda_env.py's step() exactly -- whenever
    ///    a reseed target is queued and the drone is in STATE_SEEDING, the
    ///    environment itself overrides the policy's chosen movement
    ///    direction with a direct line toward the NEAREST queued target
    ///    (rwanda_env.py: min(reseeding_targets, key=manhattan_dist)), only
    ///    overriding species once the drone actually arrives at the target
    ///    cell.
    ///
    /// 2. Coverage sweep: a scripted serpentine/boustrophedon sweep across
    ///    the whole zone for ordinary (non-reseed) navigation. rwanda_env.py
    ///    has NO equivalent -- the trained policy's own local, patch-based
    ///    decisions drive ordinary navigation there, which in practice looks
    ///    like the drone working a local area then relocating, rather than
    ///    evenly sweeping the whole terrain. That's the more faithful
    ///    behaviour, but for the live demo the systematic sweep reads far
    ///    better (visibly covers the whole zone instead of clustering in a
    ///    few patches), so it's restored here as a deliberate demo-fidelity
    ///    trade-off, not a training-parity claim.
    public static class CoverageOverride
    {
        public static bool Enabled = true;

        private const int X_SPACING = 5;
        private const int Y_SPACING = 16;

        private static List<(int x, int y)> _targets;
        private static int _pointer;
        private static ZoneData _plannedZone;

        /* Detects a reseed target the drone can't actually make progress
           toward (most commonly one that's obstacle-blocked from every
           approach angle). rwanda_env.py has no equivalent give-up logic,
           but nothing there can get stuck in the first place -- this is a
           Unity-only safety valve for a genuinely unreachable target, not a
           training-behavior deviation. */
        private static (int x, int y) _lastReseedTarget = (-1, -1);
        private static (int x, int y) _lastDronePos = (-1, -1);
        private static int _stuckSteps = 0;
        private const int MAX_STUCK_STEPS = 6;

        public static void PlanForZone(ZoneData zone, int seedBudget)
        {
            if (zone == null) return;
            if (ReferenceEquals(zone, _plannedZone) && _targets != null) return; // same zone -- keep sweeping onward

            _plannedZone = zone;
            _targets = new List<(int, int)>();
            _pointer = 0;

            int size = zone.Size;
            int halfX = X_SPACING / 2;
            int halfY = Y_SPACING / 2;

            bool reverse = false;
            for (int y = halfY; y < size; y += Y_SPACING)
            {
                var row = new List<(int, int)>();
                for (int x = halfX; x < size; x += X_SPACING)
                    row.Add((x, y));
                if (reverse) row.Reverse(); // serpentine/boustrophedon sweep
                reverse = !reverse;
                _targets.AddRange(row);
            }
        }

        public static bool TryGetOverrideAction(EpisodeState s, out int action, out bool suppressSeeding)
        {
            action = 0;
            suppressSeeding = false;
            if (!Enabled) return false;

            if (s.SeedsRemaining <= 0) return false;

            if (s.ReseedingTargets.Count > 0)
            {
                /* Nearest queued target by Manhattan distance, matching
                   rwanda_env.py's min(reseeding_targets, key=lambda t: abs(t[0]-y)+abs(t[1]-x))
                   exactly -- not just the first one found in iteration order. */
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
                    /* Fall through to the coverage sweep this step instead. */
                }
                else
                {
                    int? recommended = s.ReseedSpeciesMap.TryGetValue(target, out int rec) ? rec : (int?)null;
                    return TryStepToward(s, target.x, target.y, out action, out suppressSeeding, recommended);
                }
            }

            if (_targets == null || _targets.Count == 0) return false;

            while (_pointer < _targets.Count && _targets[_pointer] == (s.X, s.Y))
                _pointer++;

            if (_pointer >= _targets.Count) return false;

            var (tx, ty) = _targets[_pointer];
            return TryStepToward(s, tx, ty, out action, out suppressSeeding);
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
            /* Reseed targets carry MonitoringSystem's better-suited species recommendation. */
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

        /* Deterministically picks among the species whose rain_min this
           cell's rainfall actually clears -- the x*7+y*13 hash just spreads
           the choice across cells (so nearby cells don't all pick the same
           species) without needing an RNG instance threaded through this
           static class. */
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
            /* _targets/_pointer/_plannedZone deliberately NOT reset here --
               PlanForZone() only replans when the zone actually changes, so
               a mid-mission reseed-triggered Reset() shouldn't restart the
               sweep from the top. */
        }
    }
}
