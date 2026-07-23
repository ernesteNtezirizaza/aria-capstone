namespace ARIA.Core
{
    public static class ARIAConstants
    {
        // Zone / grid
        public const int ZONE_SIZE   = 120;   // each zone = 120x120 cells
        public const int OBS_WINDOW  = 11;    // local terrain patch size
        public const int N_CHANNELS  = 5;     // terrain_window channels
        public const int N_SPECIES   = 5;
        public const int N_ACTIONS   = 47;

        // Action indices
        public const int HOVER_ACTION  = 40;
        public const int ABORT_ACTION  = 41;
        public const int COVER_DEPLOY  = 42;
        public const int COVER_RETRACT = 43;
        public const int ALT_UP        = 44;
        public const int ALT_DOWN      = 45;
        public const int EMERGENCY     = 46;

        // Drone state machine
        public const int STATE_GROUNDED   = 0;
        public const int STATE_TAKEOFF    = 1;
        public const int STATE_NAVIGATING = 2;
        public const int STATE_SEEDING    = 3;
        public const int STATE_RETURNING  = 4;
        public const int STATE_LANDING    = 5;
        public const int STATE_OBSTACLE   = 6;
        public const int N_STATES         = 7;

        // Episode
        public const int   MAX_STEPS     = 1000;
        public const float INITIAL_SEEDS = 500f; // used to normalise seeds_remaining in drone_vector

        // Battery / energy
        public const float BATTERY_MAX           = 1.0f;
        public const float BATTERY_INIT          = 1.0f;
        public const float BATTERY_DRAIN_SUNNY   = 0.002f;   // per step in sun
        public const float BATTERY_DRAIN_RAIN    = 0.004f;   // per step in rain (2x drain)
        public const float SOLAR_CHARGE_RATE     = 0.002f;   // per step when sunny
        public const float BATTERY_RETURN_THRESH = 0.05f;    // return to base below this
        public const float BATTERY_CRITICAL      = 0.00f;    // emergency land below this
        public const int   RETURN_DESCENT_RANGE  = 8;         // start descending this many cells out from base

        // Weather
        public const int WEATHER_SUNNY = 0;
        public const int WEATHER_RAINY = 1;

        public const float RAINFALL_SUNNY_THRESH = 0.266f;
        public const float ZONE_MIN_SOIL         = 0.358f;

        // Mirrors configs.config.ZONE_SUITABILITY_WEIGHTS in the Python
        // training side -- soil/rain/slope combine into one zone-level
        // suitability score with these weights, both here and in the
        // reward function ZoneData.ZoneSuitability() feeds.
        public const float ZONE_SUIT_W_SOIL  = 3.0f;
        public const float ZONE_SUIT_W_RAIN  = 2.0f;
        public const float ZONE_SUIT_W_SLOPE = 1.0f;

        // Mirrors configs.config.ZONE_MIN_SUITABILITY (P25 of the composite
        // soil+rain-slope score over the real raw dataset). This value is
        // ESTIMATED, not derived from your actual full-resolution dataset
        // the way ZONE_MIN_SOIL above was -- this sandbox could only run
        // the real pipeline against a downsampled copy of your rasters
        // (full resolution ran out of memory here). After your next Kaggle
        // run, Cell 6 of the notebook now prints the real
        // config.ZONE_MIN_SUITABILITY value -- replace this constant with
        // that number.
        public const float ZONE_MIN_SUITABILITY = 0.24f;

        public const int MIN_SEED_SPACING = 3;

        public const int N_SEASONS = 6;
        public static readonly int SEASON_LENGTH = MAX_STEPS / N_SEASONS;

        public static readonly (int dy, int dx)[] DIRECTIONS = new (int, int)[]
        {
            (-1,  0), // 0: N
            ( 1,  0), // 1: S
            ( 0,  1), // 2: E
            ( 0, -1), // 3: W
            (-1,  1), // 4: NE
            (-1, -1), // 5: NW
            ( 1,  1), // 6: SE
            ( 1, -1), // 7: SW
        };

        public static readonly string[] SPECIES_NAMES = new string[]
        {
            "Eucalyptus globulus",    // 0 -- common: Inturusu
            "Grevillea robusta",      // 1 -- common: Gereveriya
            "Eucalyptus maculata",    // 2 -- common: Inturusu
            "Eucalyptus maidenii",    // 3 -- common: Ruvuvu
            "Artocarpus heterophyllus", // 4 -- common: Igifenesi
        };

        public static readonly float[] SPECIES_RAIN_MIN = new float[]
        {
            0.0848f, 0.1018f, 0.1188f, 0.1358f, 0.1697f,
        };

        public const float OBSTACLE_THRESHOLD     = 0.7f;
        public const float OBSTACLE_SAFE_ALTITUDE = 0.5f;

        public const float PROTECTED_PROXIMITY_THRESHOLD = 0.9f; // in_p = prox >= 0.9
    }
}
