"""
configs/config.py
=================
Single source of truth for the entire ARIA ML pipeline.
"""

import os

# ── Paths ─────────────────────────────────────────────────────────
ROOT_DIR        = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DATA_RAW_DIR    = os.path.join(ROOT_DIR, "data", "raw")
DATA_PROC_DIR   = os.path.join(ROOT_DIR, "data", "processed")
ZONES_DIR       = os.path.join(ROOT_DIR, "data", "zones")
RESULTS_DIR     = os.path.join(ROOT_DIR, "results")
CHECKPOINTS_DIR = os.path.join(RESULTS_DIR, "checkpoints")
PLOTS_DIR       = os.path.join(RESULTS_DIR, "plots")
METRICS_DIR     = os.path.join(RESULTS_DIR, "metrics")

# ── Raw dataset paths ─────────────────────────────────────────────
DEM_PATH       = os.path.join(DATA_RAW_DIR, "dem", "rwanda_dem_250m.tif")
SOIL_CLAY_PATH = os.path.join(DATA_RAW_DIR, "soil", "rwanda_clay_250m.tif")
SOIL_PH_PATH   = os.path.join(DATA_RAW_DIR, "soil", "rwanda_phh2o_250m.tif")
SOIL_SAND_PATH = os.path.join(DATA_RAW_DIR, "soil", "rwanda_sand_250m.tif")
SOIL_SOC_PATH  = os.path.join(DATA_RAW_DIR, "soil", "rwanda_soc_250m.tif")
RAINFALL_DIR   = os.path.join(DATA_RAW_DIR, "rainfall")
LANDCOVER_DIR  = os.path.join(DATA_RAW_DIR, "landcover")
WDPA_PATH      = os.path.join(
    DATA_RAW_DIR, "protected_areas", "wdpa_shp",
    "WDPA_WDOECM_Jun2026_Public_RWA_shp-polygons.shp"
)
SPECIES_PATH   = os.path.join(
    DATA_RAW_DIR, "species",
    "Interactive Suitable Tree Species Selection "
    "and Management Tool for East Africa_Rwanda Tool.xls"
)

# ── Rwanda grid ───────────────────────────────────────────────────
RWANDA_BOUNDS = {
    "left": 28.84, "bottom": -2.84,
    "right": 30.90, "top": -1.04,
}
GRID_COLS    = 902
GRID_ROWS    = 745
RESOLUTION_M = 250
ZONE_SIZE    = 120   # each zone = 120×120 cells = 30km×30km

# ── Terrain channels ──────────────────────────────────────────────
CH_ELEVATION  = 0
CH_SLOPE      = 1
CH_SOIL       = 2
CH_RAINFALL   = 3
CH_LANDCOVER  = 4
N_CHANNELS    = 5

# ── Observation window ────────────────────────────────────────────
OBS_WINDOW = 11   # 11×11 patch centred on drone

# ── Species ───────────────────────────────────────────────────────
SPECIES = {
    0: {"name": "Eucalyptus grandis",  "common": "Eucalyptus",
        "germ_steps": 8,  "mature_steps": 60,  "rain_min": 0.30},
    1: {"name": "Markhamia lutea",     "common": "Nile Tulip",
        "germ_steps": 10, "mature_steps": 80,  "rain_min": 0.40},
    2: {"name": "Albizia gummifera",   "common": "Peacock Flower",
        "germ_steps": 15, "mature_steps": 100, "rain_min": 0.45},
    3: {"name": "Maesopsis eminii",    "common": "Musizi",
        "germ_steps": 20, "mature_steps": 120, "rain_min": 0.50},
    4: {"name": "Prunus africana",     "common": "African Cherry",
        "germ_steps": 25, "mature_steps": 150, "rain_min": 0.55},
}
N_SPECIES = len(SPECIES)

# ── Actions ───────────────────────────────────────────────────────
# 0-39  : move(8 dirs) × drop(5 species)
# 40    : hover
# 41    : abort mission → return to base
# 42    : deploy rain cover
# 43    : retract rain cover
# 44    : increase altitude (obstacle avoidance)
# 45    : decrease altitude
# 46    : emergency land
N_ACTIONS    = 47
HOVER_ACTION = 40
ABORT_ACTION = 41
COVER_DEPLOY = 42
COVER_RETRACT= 43
ALT_UP       = 44
ALT_DOWN     = 45
EMERGENCY    = 46

DIRECTIONS = {
    0: (-1,  0),   # N
    1: ( 1,  0),   # S
    2: ( 0,  1),   # E
    3: ( 0, -1),   # W
    4: (-1,  1),   # NE
    5: (-1, -1),   # NW
    6: ( 1,  1),   # SE
    7: ( 1, -1),   # SW
}

# ── Drone states ──────────────────────────────────────────────────
STATE_GROUNDED   = 0
STATE_TAKEOFF    = 1
STATE_NAVIGATING = 2
STATE_SEEDING    = 3
STATE_RETURNING  = 4
STATE_LANDING    = 5
STATE_OBSTACLE   = 6

# ── Episode ───────────────────────────────────────────────────────
MAX_STEPS           = 500
INITIAL_SEEDS       = 200
MONITORING_INTERVAL = 10
MAX_SLOPE_DEG       = 30.0
MIN_SEED_SPACING    = 3

# ── Energy system ─────────────────────────────────────────────────
BATTERY_MAX          = 1.0
BATTERY_INIT         = 1.0
BATTERY_DRAIN_SUNNY  = 0.002   # per step in sun
BATTERY_DRAIN_RAIN   = 0.004   # per step in rain (2× drain)
SOLAR_CHARGE_RATE    = 0.0015  # per step when sunny
BATTERY_RETURN_THRESH= 0.25    # return to base when below this
BATTERY_CRITICAL     = 0.05    # emergency land when below this
RAINFALL_SUNNY_THRESH= 0.30    # below = sunny, above = rainy

# ── Weather ───────────────────────────────────────────────────────
WEATHER_SUNNY = 0
WEATHER_RAINY = 1
COVER_ACCURACY_PENALTY = 0.15  # 15% accuracy loss when cover on

# ── Zone abort thresholds ─────────────────────────────────────────
ZONE_MIN_SOIL      = 0.25
ZONE_MIN_RAIN      = 0.25
ZONE_MAX_SLOPE_PCT = 0.70   # abort if >70% of zone is no-plant
ZONE_MAX_COVERED   = 0.80   # abort if >80% already seeded

# ── Rainfall seasons ──────────────────────────────────────────────
RAINFALL_FILES = [
    "chirps-v2.0.2021.03.tif",
    "chirps-v2.0.2021.04.tif",
    "chirps-v2.0.2021.05.tif",
    "chirps-v2.0.2022.03.tif",
    "chirps-v2.0.2022.04.tif",
    "chirps-v2.0.2022.05.tif",
]
N_SEASONS = len(RAINFALL_FILES)

# ── Zones ─────────────────────────────────────────────────────────
ZONE_DEFINITIONS = [
    (1,  1, 0, "Virunga Foothills North",  "Northern Highlands", "train"),
    (2,  3, 0, "Northern Plateau West",    "Northern Highlands", "train"),
    (3,  5, 0, "Northern Plateau East",    "Northern Highlands", "eval"),
    (4,  0, 1, "Congo-Nile Divide North",  "Western Ridge",      "train"),
    (5,  0, 3, "Congo-Nile Divide Central","Western Ridge",      "train"),
    (6,  0, 4, "Congo-Nile Divide South",  "Western Ridge",      "eval"),
    (7,  2, 1, "Central Plateau NW",       "Central Plateau",    "train"),
    (8,  3, 2, "Central Plateau Core",     "Central Plateau",    "train"),
    (9,  4, 2, "Central Plateau East",     "Central Plateau",    "eval"),
    (10, 1, 3, "Kivu Belt North",          "Kivu Belt",          "train"),
    (11, 2, 4, "Kivu Belt South",          "Kivu Belt",          "train"),
    (12, 1, 4, "Kivu Belt Southwest",      "Kivu Belt",          "eval"),
    (13, 5, 2, "Eastern Savanna North",    "Eastern Savanna",    "train"),
    (14, 6, 3, "Eastern Savanna Central",  "Eastern Savanna",    "train"),
    (15, 5, 4, "Eastern Savanna South",    "Eastern Savanna",    "eval"),
    (16, 2, 5, "Southern Valley West",     "Southern Valley",    "train"),
    (17, 4, 5, "Southern Valley Central",  "Southern Valley",    "train"),
    (18, 6, 5, "Southern Valley East",     "Southern Valley",    "eval"),
]
TRAIN_ZONE_IDS = [z[0] for z in ZONE_DEFINITIONS if z[5] == "train"]
EVAL_ZONE_IDS  = [z[0] for z in ZONE_DEFINITIONS if z[5] == "eval"]

# ── Reward weights ────────────────────────────────────────────────
REWARD = {
    "w_soil":          1.5,
    "w_rain":          1.2,
    "w_slope":         1.0,
    "w_spacing":       0.8,
    "w_protected":    10.0,
    "w_disturbance":   0.6,
    "w_germ":          2.0,
    "w_diversity":     0.5,
    "w_reseed":        3.0,
    "step_penalty":    0.01,
    "battery_save":    1.0,
    "bad_abort":      -2.0,
    "battery_empty":  -5.0,
    "obstacle_clear":  0.5,
    "obstacle_hit":   -3.0,
    "cover_correct":   0.1,
    "cover_wrong":    -0.1,
}
DISTURBANCE_BASE_PROB = 0.30

# ── Training ──────────────────────────────────────────────────────
TOTAL_TIMESTEPS = 50_000
N_ENVS          = 4
EVAL_FREQ       = 5_000
N_EVAL_EPISODES = 5
DISCOUNT_GAMMA  = 0.99

# ── Evaluation metrics ────────────────────────────────────────────
PRIMARY_METRIC     = "pct_suitable_seeded"
TARGET_IMPROVEMENT = 0.20
EVAL_METRICS = [
    "pct_suitable_seeded",
    "mean_soil_score",
    "species_entropy",
    "spacing_violations",
    "protected_area_seeds",
    "seasonal_rain_score",
    "reseeding_count",
    "missions_completed",
    "battery_empty_events",
    "obstacles_avoided",
]
