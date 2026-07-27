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

        // Returns the real disturbance-tier reward for this tick, mirroring
        // disturbance_engine.py's step(): each actual kill pays growth.Kill's
        // delayed-death penalty plus an extra -w_disturbance.
        public float Step(GrowthEngine growth, int timestep)
        {
            float reward = 0f;
            var alive = growth.Alive();
            if (alive.Count == 0) return reward;

            // Corridor proximity is 0 for most seeds, so guarantee one real kill per check.
            reward += Kill(growth, alive[_rng.Next(alive.Count)], timestep);

            foreach (var seed in alive)
            {
                float p = DISTURBANCE_BASE_PROB * seed.CorridorProximity;
                if (p > 0f && (float)_rng.NextDouble() < p)
                    reward += Kill(growth, seed, timestep);
            }
            return reward;
        }

        private float Kill(GrowthEngine growth, Seed seed, int timestep)
        {
            if (seed.Stage == SeedStage.Dead) return 0f; // may have just been killed above
            float penalty = growth.Kill(seed.SeedId, timestep, "disturbance");
            Events.Add(new DisturbanceEvent
            {
                SeedId = seed.SeedId,
                X = seed.X, Y = seed.Y,
                Timestep = timestep,
                Proximity = seed.CorridorProximity,
            });
            return penalty - ARIAConstants.REWARD_W_DISTURBANCE;
        }
    }
}
