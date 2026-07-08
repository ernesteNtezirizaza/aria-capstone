// DisturbanceEngine.cs
// ====================
// Faithful port of env/disturbance_engine.py -- animal disturbance
// near protected area boundaries kills seeds probabilistically.

using System.Collections.Generic;
using ARIA.Core;

namespace ARIA.Systems
{
    public class DisturbanceEvent
    {
        public int SeedId;
        public int X, Y;
        public int Timestep;
        public float Proximity;
    }

    public class DisturbanceEngine
    {
        private const float DISTURBANCE_BASE_PROB = 0.30f;

        private readonly System.Random _rng;
        public List<DisturbanceEvent> Events = new List<DisturbanceEvent>();

        public DisturbanceEngine(System.Random rng = null)
        {
            _rng = rng ?? new System.Random();
        }

        public void Reset() => Events.Clear();

        public void Step(GrowthEngine growth, int timestep)
        {
            var alive = growth.Alive();
            if (alive.Count == 0) return;

            // Corridor proximity is 0 for most seeds, which zeroes out their
            // probability entirely -- so guarantee at least one real kill per
            // check while the demo toggle is on, instead of it rarely firing.
            Kill(growth, alive[_rng.Next(alive.Count)], timestep);

            foreach (var seed in alive)
            {
                float p = DISTURBANCE_BASE_PROB * seed.CorridorProximity;
                if (p > 0f && (float)_rng.NextDouble() < p)
                    Kill(growth, seed, timestep);
            }
        }

        private void Kill(GrowthEngine growth, Seed seed, int timestep)
        {
            if (seed.Stage == SeedStage.Dead) return; // may have just been killed above
            growth.Kill(seed.SeedId, timestep, "disturbance");
            Events.Add(new DisturbanceEvent
            {
                SeedId = seed.SeedId,
                X = seed.X, Y = seed.Y,
                Timestep = timestep,
                Proximity = seed.CorridorProximity,
            });
        }
    }
}
