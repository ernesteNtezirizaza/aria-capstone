using ARIA.Core;

namespace ARIA.ML
{
    public static class ActionSelector
    {
        public static int SelectArgmax(float[] logits)
        {
            int best = 0;
            float bestVal = float.NegativeInfinity;
            for (int i = 0; i < logits.Length; i++)
            {
                if (logits[i] > bestVal)
                {
                    bestVal = logits[i];
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// Decodes an action index (0-46) into a human-readable
        /// description, matching the action layout documented in
        /// rwanda_env.py / config.py exactly:
        ///   0-39  = move(8 dirs) x seed(5 species)
        ///   40    = hover
        ///   41    = abort -> return to base
        ///   42    = deploy rain cover
        ///   43    = retract rain cover
        ///   44    = increase altitude (obstacle avoidance)
        ///   45    = decrease altitude
        ///   46    = emergency land
        /// </summary>
        public static string Describe(int action)
        {
            if (action == ARIAConstants.HOVER_ACTION)  return "Hover";
            if (action == ARIAConstants.ABORT_ACTION)  return "Abort -> Return to base";
            if (action == ARIAConstants.COVER_DEPLOY)  return "Deploy rain cover";
            if (action == ARIAConstants.COVER_RETRACT) return "Retract rain cover";
            if (action == ARIAConstants.ALT_UP)        return "Increase altitude";
            if (action == ARIAConstants.ALT_DOWN)      return "Decrease altitude";
            if (action == ARIAConstants.EMERGENCY)     return "EMERGENCY LAND";

            if (action >= 0 && action < 40)
            {
                int dirIdx    = action / ARIAConstants.N_SPECIES;
                int speciesId = action % ARIAConstants.N_SPECIES;
                string dirName = DirectionName(dirIdx);
                string species = speciesId < ARIAConstants.SPECIES_NAMES.Length
                    ? ARIAConstants.SPECIES_NAMES[speciesId]
                    : $"species {speciesId}";
                return $"Move {dirName} + drop {species}";
            }

            return $"Unknown action {action}";
        }

        private static string DirectionName(int dirIdx)
        {
            switch (dirIdx)
            {
                case 0: return "N";
                case 1: return "S";
                case 2: return "E";
                case 3: return "W";
                case 4: return "NE";
                case 5: return "NW";
                case 6: return "SE";
                case 7: return "SW";
                default: return $"dir{dirIdx}";
            }
        }
    }
}
