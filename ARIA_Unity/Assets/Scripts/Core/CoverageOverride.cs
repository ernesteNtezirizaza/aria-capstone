using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARIA.Core
{
    public static class CoverageOverride
    {
        public static bool Enabled = true;

        private const int X_SPACING = 5;
        private const int Y_SPACING = 16;

        private static List<(int x, int y)> _targets;
        private static int _pointer;
        private static ZoneData _plannedZone;

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
                    row.Add((x, y)); // full-grid sweep -- see COVERAGE FIX note above
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
                (int y, int x) target = default;
                foreach (var t in s.ReseedingTargets) { target = t; break; } // any queued target -- HashSet has no order guarantee, first is fine
                return TryStepToward(s, target.x, target.y, out action, out suppressSeeding);
            }

            if (_targets == null || _targets.Count == 0) return false;

            while (_pointer < _targets.Count && _targets[_pointer] == (s.X, s.Y))
                _pointer++;

            if (_pointer >= _targets.Count) return false;

            var (tx, ty) = _targets[_pointer];
            return TryStepToward(s, tx, ty, out action, out suppressSeeding);
        }

        private static bool TryStepToward(EpisodeState s, int tx, int ty, out int action, out bool suppressSeeding)
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
            int speciesId = BestSpeciesFor(s.Zone, tx, ty);
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

        // Real rainfall data is a smoothly-interpolated, low-frequency field (bilinear
        // resample of a coarse satellite product), so always picking the single most
        // rain-demanding species a cell can support collapses to one species across
        // huge contiguous areas of a zone. Instead, rotate deterministically by grid
        // position through every species the cell's rainfall can actually support --
        // still ecologically constrained (never plants a species short on rain), but
        // visibly mixes the 5 species instead of monoculture patches.
        private static int BestSpeciesFor(ZoneData zone, int x, int y)
        {
            float rain = zone.Terrain[y, x, 3];
            if (float.IsNaN(rain)) rain = 0f;

            Span<int> eligible = stackalloc int[ARIAConstants.N_SPECIES];
            int count = 0;
            for (int i = 0; i < ARIAConstants.N_SPECIES; i++)
                if (rain >= ARIAConstants.SPECIES_RAIN_MIN[i]) eligible[count++] = i;
            if (count == 0) return 0;

            int idx = (x * 7 + y * 13) % count;
            return eligible[idx];
        }

        public static void Reset()
        {
            _targets = null;
            _pointer = 0;
            _plannedZone = null;
        }
    }
}
