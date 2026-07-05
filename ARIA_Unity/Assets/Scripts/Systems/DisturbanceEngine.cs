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
            foreach (var seed in growth.Living())
            {
                float p = DISTURBANCE_BASE_PROB * seed.CorridorProximity;
                if (p > 0f && (float)_rng.NextDouble() < p)
                {
                    growth.Kill(seed.SeedId, timestep, "disturbance");
                    var e = new DisturbanceEvent
                    {
                        SeedId = seed.SeedId,
                        X = seed.X, Y = seed.Y,
                        Timestep = timestep,
                        Proximity = seed.CorridorProximity,
                    };
                    Events.Add(e);
                }
            }
        }
    }
}
