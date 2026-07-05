using System.Collections.Generic;
using System.Linq;
using ARIA.Core;

namespace ARIA.Systems
{
    public class MonitoringSystem
    {
        public List<FailedCell> FailedCellsLog = new List<FailedCell>();
        public List<FailedCell> ReseedQueue     = new List<FailedCell>();
        public List<(int x, int y)> ReseedLog   = new List<(int, int)>();

        public void Reset() { /* deliberately empty -- see docstring */ }

        public void FullReset()
        {
            FailedCellsLog.Clear();
            ReseedQueue.Clear();
            ReseedLog.Clear();
        }

        public void IngestFailures(List<FailedCell> failedCells)
        {
            foreach (var fc in failedCells)
            {
                bool exists = ReseedQueue.Any(r => r.X == fc.X && r.Y == fc.Y);
                if (!exists)
                {
                    fc.RecommendedSpecies = Recommend(fc);
                    fc.Priority = Priority(fc);
                    ReseedQueue.Add(fc);
                }
                FailedCellsLog.Add(fc);
            }

            // Sort by priority, highest first
            ReseedQueue = ReseedQueue.OrderByDescending(r => r.Priority).ToList();
        }

        private int Recommend(FailedCell fc)
        {
            if (fc.Reason == "disturbance" || fc.Rain < 0.35f)
                return 0;
            return System.Math.Min(fc.SpeciesTried + 1, ARIAConstants.N_SPECIES - 1);
        }

        private float Priority(FailedCell fc) => (fc.Soil + fc.Rain) / 2f;

        public List<FailedCell> GetTopTargets(int n = 5)
        {
            return ReseedQueue.Take(n).ToList();
        }

        /// <summary>Remove a cell from the queue when the drone reseeds it.</summary>
        public void MarkReseeded(int x, int y)
        {
            ReseedQueue.RemoveAll(r => r.X == x && r.Y == y);
            ReseedLog.Add((x, y));
        }

        public int QueueSize() => ReseedQueue.Count;
    }
}
