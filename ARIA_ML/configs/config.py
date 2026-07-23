"""
configs/config.py
=================
Single source of truth for the entire ARIA ML pipeline.

"""

import os

# ── Paths ─────────────────────────────────────────────────────────
ROOT_DIR        = "/kaggle/working/ARIA_ML"
DATA_RAW_DIR    = os.path.join(ROOT_DIR, "data", "raw")
DATA_PROC_DIR   = os.path.join(ROOT_DIR, "data", "processed")
ZONES_DIR       = os.path.join(ROOT_DIR, "data", "zones")
RESULTS_DIR     = os.path.join(ROOT_DIR, "results")
CHECKPOINTS_DIR = os.path.join(RESULTS_DIR, "checkpoints")
PLOTS_DIR       = os.path.join(RESULTS_DIR, "plots")
METRICS_DIR     = os.path.join(RESULTS_DIR, "metrics")

# ── Raw dataset paths ─────────────────────────────────────────────
DEM_PATH       = os.path.join(DATA_RAW_DIR, "dem", "Rwanda_SRTM30meters", "Rwanda_SRTM30meters.tif")
# Soil layers are discovered dynamically (glob, not a fixed list) so adding
# a new layer -- e.g. rwanda_soil_nitrogen.tif, rwanda_soil_carbon.tif --
# just means dropping the file in data/raw/soil/ and re-running preprocess.py.
# A hardcoded list here previously meant new files were silently ignored
# until someone remembered to add them by name in two separate places.
SOIL_DIR       = os.path.join(DATA_RAW_DIR, "soil")
RAINFALL_DIR   = os.path.join(DATA_RAW_DIR, "rainfall")
LANDCOVER_DIR  = os.path.join(DATA_RAW_DIR, "landcover")
WDPA_PATHS     = [
    os.path.join(DATA_RAW_DIR, "protected_areas",
                 "WDPA_WDOECM_Jun2026_Public_RWA_shp_0",
                 "WDPA_WDOECM_Jun2026_Public_RWA_shp-polygons.shp"),
    os.path.join(DATA_RAW_DIR, "protected_areas",
                 "WDPA_WDOECM_Jun2026_Public_RWA_shp_1",
                 "WDPA_WDOECM_Jun2026_Public_RWA_shp-polygons.shp"),
    os.path.join(DATA_RAW_DIR, "protected_areas",
                 "WDPA_WDOECM_Jun2026_Public_RWA_shp_2",
                 "WDPA_WDOECM_Jun2026_Public_RWA_shp-polygons.shp"),
]
WDPA_PATH = WDPA_PATHS[0]  # backward compat
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

# ── Zone-suitability weights ────────────────────────────────────────
# Single source of truth for how soil / rainfall / slope combine into a
# zone-level suitability score. Used both when deriving ZONE_MIN_SUITABILITY
# below (from the real raw dataset) and inside REWARD further down, so the
# per-seed placement reward and the zone-level abort/selection decision are
# always scored with the exact same ecological weighting.
ZONE_SUITABILITY_WEIGHTS = {"soil": 3.0, "rain": 2.0, "slope": 1.0}

# ── Species ───────────────────────────────────────────────────────

# ── Species loader ────────────────────────────────────────────────
def _load_species_from_dataset(global_max_mm):
    """
    Load 5 species from the Rwanda Suitable Tree Species Excel dataset.

    """
    import re as _re
    import numpy as _np

    def _parse_rain_min(raw):
        # Strip ALL whitespace to handle corrupted values like "700-1 800"
        raw = _re.sub(r"\s+", "", str(raw).strip()).replace(",", "")
        m = _re.search(r"(\d+)-\d+", raw)
        if m: return int(m.group(1))
        m = _re.match(r">(\d+)", raw)
        if m: return int(m.group(1))
        m = _re.match(r"^(\d+\.?\d*)$", raw)
        if m: return int(float(m.group(1)))
        return None

    def _growth_params(raw):
        raw = str(raw).strip().lower()
        if "fast"   in raw: return 8,  60
        if "slow"   in raw: return 25, 150
        if "medium" in raw: return 15, 100
        return 12, 80

    print(f"[SPECIES] Reading from: {SPECIES_PATH}", flush=True)

    try:
        import xlrd as _xlrd
        wb    = _xlrd.open_workbook(SPECIES_PATH)
        bio   = wb.sheet_by_name("Bio_Physical_Profiles")
        utils = wb.sheet_by_name("Tree_Utilities")
        print(f"[SPECIES] Bio={bio.nrows}r  Utils={utils.nrows}r x {utils.ncols}c", flush=True)

        # col 1 = Kinyarwanda, col 2 = Bugesera, col 3 = Gishwati, col 35 = Woodlot
        util_data = {}
        for r in range(2, utils.nrows):
            name  = str(utils.cell(r, 0).value).strip()
            kinya = str(utils.cell(r, 1).value).strip()
            if not name:
                continue
            util_data[name] = {
                "kinyarwanda": kinya if kinya and kinya.lower() not in ("", "unknown") else None,
                "rwanda":      (str(utils.cell(r, 2).value).strip().lower() == "x"
                                or str(utils.cell(r, 3).value).strip().lower() == "x"),
                "woodlot":     str(utils.cell(r, 35).value).strip().lower() == "x",
            }

        # Build woodlot candidates from Bio_Physical_Profiles
        all_woodlot = []
        for r in range(2, bio.nrows):
            name      = str(bio.cell(r, 0).value).strip()
            rain_min  = _parse_rain_min(bio.cell(r, 3).value)
            germ, mat = _growth_params(bio.cell(r, 6).value)
            util      = util_data.get(name, {})
            if (util.get("rwanda")
                    and util.get("woodlot")
                    and util.get("kinyarwanda")
                    and rain_min is not None
                    and 200 <= rain_min <= 1500):
                all_woodlot.append({
                    "name":         name,
                    "kinyarwanda":  util["kinyarwanda"],
                    "rain_min_mm":  rain_min,
                    "germ_steps":   germ,
                    "mature_steps": mat,
                })

        all_woodlot.sort(key=lambda x: x["rain_min_mm"])
        print(f"[SPECIES] Woodlot candidates: {len(all_woodlot)}", flush=True)
        for _c in all_woodlot:
            print(f"  {_c['name']:<35} rain={_c['rain_min_mm']}mm  ({_c['kinyarwanda']})", flush=True)

        # Deduplicate by rain_min (one per band)
        seen_rain, unique, duplicates = set(), [], []
        for c in all_woodlot:
            if c["rain_min_mm"] not in seen_rain:
                seen_rain.add(c["rain_min_mm"])
                unique.append(c)
            else:
                duplicates.append(c)

        # Gap-fill from duplicates if < 5 unique bands
        if len(unique) < 5:
            for c in duplicates:
                unique.append(c)
                if len(unique) >= 5:
                    break
            unique.sort(key=lambda x: x["rain_min_mm"])

        # Select 5 at P10/P30/P50/P70/P90
        n       = len(unique)
        indices = [int(_np.percentile(range(n), p)) for p in [10, 30, 50, 70, 90]]
        seen_idx, final_indices = set(), []
        for idx in indices:
            while idx in seen_idx and idx < n - 1:
                idx += 1
            seen_idx.add(idx)
            final_indices.append(idx)
        selected = [unique[i] for i in final_indices]

        species = {}
        for i, sp in enumerate(selected):
            norm = round(sp["rain_min_mm"] / 12.0 / global_max_mm, 4)
            species[i] = {
                "name":         sp["name"],
                "common":       sp["kinyarwanda"],
                "germ_steps":   sp["germ_steps"],
                "mature_steps": sp["mature_steps"],
                "rain_min":     norm,
            }

        print(f"[SPECIES] Selected:", flush=True)
        for _i, _sp in species.items():
            print(f"  sp{_i}: {_sp['name']} ({_sp['common']}) rain_min={_sp['rain_min']}", flush=True)
        return species

    except Exception as _exc:
        import traceback as _tb
        print(f"[SPECIES] ERROR: {_exc}", flush=True)
        _tb.print_exc()
        return {
            0: {"name": "Eucalyptus globulus",       "common": "Inturusu",
                "germ_steps": 8,  "mature_steps": 60,  "rain_min": 0.0850},
            1: {"name": "Grevillea robusta",          "common": "Gereveriya",
                "germ_steps": 8,  "mature_steps": 60,  "rain_min": 0.1020},
            2: {"name": "Eucalyptus maculata",        "common": "Inturusu",
                "germ_steps": 8,  "mature_steps": 60,  "rain_min": 0.1190},
            3: {"name": "Eucalyptus maidenii",        "common": "Ruvuvu",
                "germ_steps": 8,  "mature_steps": 60,  "rain_min": 0.1360},
            4: {"name": "Artocarpus heterophyllus",   "common": "Igifenesi",
                "germ_steps": 8,  "mature_steps": 60,  "rain_min": 0.1700},
        }


def _derive_from_data():
    """
    Derive all data-dependent constants from the raw dataset files.
    Called once at import time. Returns a dict of derived values.
    Falls back to safe defaults if files are not yet available.
    """
    import numpy as _np
    import os as _os
    import warnings as _warnings

    try:
        import rasterio as _rio
        from rasterio.warp   import reproject as _reproject, Resampling as _RS
        from rasterio.transform import from_bounds as _fb
        import glob as _glob

        # ── 1. DEM → grid dimensions, bounds, elevation range ─────
        dem_path = DEM_PATH
        with _rio.open(dem_path) as _src:
            _rows  = _src.height
            _cols  = _src.width
            _bnds  = _src.bounds
            _dem   = _src.read(1).astype(float)
            _res_deg = _src.res[0]

        _valid_elev = _dem[_dem > 0]
        _elev_min   = float(_valid_elev.min())
        _elev_max   = float(_dem.max())

        # Resolution in metres at Rwanda's mean latitude
        import math as _math
        _lat_mid   = (_bnds.bottom + _bnds.top) / 2.0
        _res_m     = int(round(_res_deg * 111320 * _math.cos(_math.radians(_lat_mid))))

        # ── 2. Slope → MAX_SLOPE_DEG (P95 of Rwanda slope) ────────
        _dy, _dx   = _np.gradient(_dem, _res_m, _res_m)
        _slope_deg = _np.degrees(_np.arctan(_np.sqrt(_dx**2 + _dy**2)))
        _max_slope = round(float(_np.percentile(_slope_deg, 95)), 1)

        # ── 3. CHIRPS rainfall → GLOBAL_MAX, thresholds ───────────
        _rain_files = sorted(_glob.glob(_os.path.join(RAINFALL_DIR, "chirps-v2.0.*.tif")))
        _REF_T = _fb(_bnds.left, _bnds.bottom, _bnds.right, _bnds.top, _cols, _rows)
        _stack = []
        for _rf in _rain_files:
            with _rio.open(_rf) as _rs:
                _dst = _np.zeros((_rows, _cols), dtype=_np.float32)
                _reproject(
                    source=_rio.band(_rs, 1), destination=_dst,
                    src_transform=_rs.transform, src_crs=_rs.crs,
                    dst_transform=_REF_T, dst_crs="EPSG:4326",
                    resampling=_RS.bilinear
                )
                _dst[_dst < 0] = 0.0
                _stack.append(_dst)

        _rain_stack = _np.stack(_stack, 0)
        _gmax       = float(_np.nanmax(_rain_stack))
        _rain_norm  = _rain_stack / _gmax
        _flat       = _rain_norm[_rain_norm > 0].ravel()

        _sunny_thresh = round(float(_np.percentile(_flat, 50)), 4)
        _zone_min_rain = round(float(_np.percentile(_flat, 25)), 4)

        # ── 4. Soil → ZONE_MIN_SOIL (P25 of composite soil score)
        _soil_paths = sorted(_glob.glob(_os.path.join(SOIL_DIR, "rwanda_soil_*.tif")))
        # Nitrogen excluded at discovery, not later -- see the matching
        # comment in utils/preprocess.py for why.
        _soil_paths = [p for p in _soil_paths if "nitrogen" not in _os.path.basename(p).lower()]
        if not _soil_paths:
            raise FileNotFoundError(f"No rwanda_soil_*.tif files found in {SOIL_DIR}")
        print(f"  Soil layers found ({len(_soil_paths)}): "
              f"{[_os.path.basename(p) for p in _soil_paths]}")
        _soil_layers = []
        _excluded_layers = []
        for _sp in _soil_paths:
            with _rio.open(_sp) as _ss:
                _sd_raw = _np.zeros((_rows, _cols), dtype=_np.float32)
                _reproject(
                    source=_rio.band(_ss, 1), destination=_sd_raw,
                    src_transform=_ss.transform, src_crs=_ss.crs,
                    dst_transform=_REF_T, dst_crs="EPSG:4326",
                    resampling=_RS.bilinear,
                    src_nodata=_ss.nodata, dst_nodata=_np.nan,
                )
                _sd = _sd_raw.astype(float)
                _sd[_sd <= 0] = _np.nan
                _valid_frac = _np.isfinite(_sd).mean()
                if _valid_frac < 0.5:
                    # More than half the country came back NaN after
                    # reprojection -- a real CRS/extent mismatch (or a
                    # genuine source-coverage gap), not something to
                    # silently fold into the composite. Averaging a layer
                    # that's mostly missing doesn't corrupt the composite
                    # everywhere (nanmean already skips NaN per-pixel), but
                    # it does mean this layer contributes almost nothing
                    # while still being counted as "6 layers" -- excluding
                    # it outright is more honest than pretending it helped.
                    print(f"  EXCLUDED {_os.path.basename(_sp)} from soil composite "
                          f"({100*(1-_valid_frac):.1f}% invalid)")
                    _excluded_layers.append(_os.path.basename(_sp))
                    continue
                _mn, _mx = _np.nanmin(_sd), _np.nanmax(_sd)
                if _mx > _mn:
                    _sd = (_sd - _mn) / (_mx - _mn)
                _soil_layers.append(_sd)
        if _excluded_layers:
            print(f"  Soil composite built from {len(_soil_layers)} layers "
                  f"(excluded: {_excluded_layers})")
        # Some pixels (e.g. Lake Kivu, other water bodies) are legitimately
        # NaN in every soil layer at once -- there's no soil to measure
        # there. nanmean's "Mean of empty slice" warning is numpy telling
        # us exactly that, correctly, not flagging a bug. Suppressed
        # deliberately rather than left as an unexplained warning in the log.
        with _warnings.catch_warnings():
            _warnings.filterwarnings("ignore", message="Mean of empty slice")
            _soil_comp = _np.nanmean(_np.stack(_soil_layers), axis=0)
        _zone_min_soil = round(float(_np.nanpercentile(_soil_comp, 25)), 4)

        # ── 4b. Composite zone suitability → ZONE_MIN_SUITABILITY ──
        # Combines soil + rainfall + slope with ZONE_SUITABILITY_WEIGHTS,
        # the same weighting used for per-seed placement reward, so the
        # zone-level abort/selection threshold is ecologically consistent
        # with per-cell scoring rather than being soil-only.
        _rain_mean_map = _np.nanmean(_rain_norm, axis=0)
        _slope_pen_map = _np.clip(_slope_deg / max(_max_slope, 1e-6), 0.0, 1.0)
        _w = ZONE_SUITABILITY_WEIGHTS
        _suit_map = (
            _w["soil"] * _soil_comp
            + _w["rain"] * _rain_mean_map
            - _w["slope"] * _slope_pen_map
        ) / (_w["soil"] + _w["rain"] + _w["slope"])
        _suit_map = _np.clip(_suit_map, 0.0, 1.0)
        _zone_min_suitability = round(float(_np.nanpercentile(_suit_map, 25)), 4)

        # ── 5. Species → from Excel dataset ───────────────────────
        _species = _load_species_from_dataset(_gmax)

        return {
            "GRID_ROWS":             _rows,
            "GRID_COLS":             _cols,
            "RESOLUTION_M":          _res_m,
            "ELEV_MIN":              _elev_min,
            "ELEV_MAX":              _elev_max,
            "MAX_SLOPE_DEG":         _max_slope,
            "GLOBAL_MAX_MONTHLY_MM": _gmax,
            "RAINFALL_SUNNY_THRESH": _sunny_thresh,
            "ZONE_MIN_RAIN":         _zone_min_rain,
            "ZONE_MIN_SOIL":         _zone_min_soil,
            "ZONE_MIN_SUITABILITY":  _zone_min_suitability,
            "SPECIES":               _species,
        }

    except Exception as _exc:
        return {
            "GRID_ROWS":             745,
            "GRID_COLS":             902,
            "RESOLUTION_M":          254,
            "ELEV_MIN":              858.0,
            "ELEV_MAX":              4440.0,
            "MAX_SLOPE_DEG":         17.7,
            "GLOBAL_MAX_MONTHLY_MM": 490.18,
            "RAINFALL_SUNNY_THRESH": 0.266,
            "ZONE_MIN_RAIN":         0.2327,
            "ZONE_MIN_SOIL":         0.358,
            "ZONE_MIN_SUITABILITY":  0.400,
            "SPECIES": {
                0: {"name": "Eucalyptus globulus",       "common": "Inturusu",
                    "germ_steps": 8,  "mature_steps": 60,  "rain_min": 0.0850},
                1: {"name": "Grevillea robusta",          "common": "Gereveriya",
                    "germ_steps": 8,  "mature_steps": 60,  "rain_min": 0.1020},
                2: {"name": "Eucalyptus maculata",        "common": "Inturusu",
                    "germ_steps": 8,  "mature_steps": 60,  "rain_min": 0.1190},
                3: {"name": "Eucalyptus maidenii",        "common": "Ruvuvu",
                    "germ_steps": 8,  "mature_steps": 60,  "rain_min": 0.1360},
                4: {"name": "Artocarpus heterophyllus",   "common": "Igifenesi",
                    "germ_steps": 8,  "mature_steps": 60,  "rain_min": 0.1700},
            },
        }




# -- Unpack data-derived constants --------------------------------------------------
_DERIVED              = _derive_from_data()
GRID_ROWS             = _DERIVED["GRID_ROWS"]
GRID_COLS             = _DERIVED["GRID_COLS"]
RESOLUTION_M          = _DERIVED["RESOLUTION_M"]
ELEV_MIN              = _DERIVED["ELEV_MIN"]
ELEV_MAX              = _DERIVED["ELEV_MAX"]
MAX_SLOPE_DEG         = _DERIVED["MAX_SLOPE_DEG"]
GLOBAL_MAX_MONTHLY_MM = _DERIVED["GLOBAL_MAX_MONTHLY_MM"]
RAINFALL_SUNNY_THRESH = _DERIVED["RAINFALL_SUNNY_THRESH"]
ZONE_MIN_RAIN         = _DERIVED["ZONE_MIN_RAIN"]
ZONE_MIN_SOIL         = _DERIVED["ZONE_MIN_SOIL"]
ZONE_MIN_SUITABILITY  = _DERIVED["ZONE_MIN_SUITABILITY"]
SPECIES               = _DERIVED["SPECIES"]

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
N_ACTIONS     = 47
HOVER_ACTION  = 40
ABORT_ACTION  = 41
COVER_DEPLOY  = 42
COVER_RETRACT = 43
ALT_UP        = 44
ALT_DOWN      = 45
EMERGENCY     = 46

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
# Both raised together, deliberately. INITIAL_SEEDS is doubled to
# directly raise the achievable ceiling on pct_suitable_seeded (the
# ceiling is seed_budget / suitable_cells_in_zone, so doubling seeds
# roughly doubles the ceiling). MAX_STEPS is raised alongside it so the
# step cap doesn't quietly become the new binding constraint instead --
# episodes were already reaching 400-460 steps on the old 500-seed
# budget, so doubling seeds without more time risked the drone simply
# running out of steps before using the extra seeds, which would measure
# a new time constraint rather than the seed-budget effect this change
# is meant to isolate.
MAX_STEPS           = 1800
INITIAL_SEEDS       = 1000
MONITORING_INTERVAL = 10
# Raised from 3: that only prevented literal stacking in a 120x120 zone,
# doing little to push toward genuine zone-wide spread. 5 cells still
# leaves ample room for the full 1000-seed budget (thousands of suitable
# cells per zone), while meaningfully widening the neighbourhood
# w_spacing's now-larger penalty (configs/config.py REWARD) actually
# covers.
MIN_SEED_SPACING    = 5

# ── Energy system ─────────────────────────────────────────────────
BATTERY_MAX           = 1.0
BATTERY_INIT          = 1.0
BATTERY_DRAIN_SUNNY   = 0.002    # per step in sun
BATTERY_DRAIN_RAIN    = 0.004    # per step in rain (2× drain)
SOLAR_CHARGE_RATE     = 0.002   # per step when sunny
BATTERY_RETURN_THRESH = 0.10     # return to base when below this
BATTERY_CRITICAL      = 0.05     # emergency land when below this

# ── Weather ───────────────────────────────────────────────────────
WEATHER_SUNNY          = 0
WEATHER_RAINY          = 1
COVER_ACCURACY_PENALTY = 0.15   # 15% accuracy loss when cover on

# ── Zone abort thresholds ─────────────────────────────────────────
ZONE_MAX_SLOPE_PCT = 0.70    # abort if >70% of zone is no-plant
ZONE_MAX_COVERED   = 0.80    # abort if >80% already seeded

# ── Rainfall seasons ──────────────────────────────────────────────
# Previously only March/April/May, repeated across 2021 and 2022 -- the
# same season type seen twice, never Rwanda's short rains (Oct-Dec) or
# either dry season. The originally planned fix (fetching the rest of
# 2021) was superseded once the real files actually landed on disk: the
# real dataset now has a full real calendar year of 2024 instead, plus
# the original 2021/2022 March-May files still present alongside it (not
# referenced here, but still picked up by the glob-based threshold
# derivation above -- see CHANGES_AND_VALIDATION.md for that tradeoff).
# WeatherSystem now cycles through a genuine full year via 2024.
RAINFALL_FILES = [
    "chirps-v2.0.2024.01.tif",
    "chirps-v2.0.2024.02.tif",
    "chirps-v2.0.2024.03.tif",
    "chirps-v2.0.2024.04.tif",
    "chirps-v2.0.2024.05.tif",
    "chirps-v2.0.2024.06.tif",
    "chirps-v2.0.2024.07.tif",
    "chirps-v2.0.2024.08.tif",
    "chirps-v2.0.2024.09.tif",
    "chirps-v2.0.2024.10.tif",
    "chirps-v2.0.2024.11.tif",
    "chirps-v2.0.2024.12.tif",
]
N_SEASONS = len(RAINFALL_FILES)

ZONE_DEFINITIONS = [
    # ── Row 0 ─────────────────────────────────────────────────────
    (19, 0, 0, "Northwest Highlands",        "Northern Highlands", "train"),
    (4,  0, 1, "Congo-Nile Divide North",    "Western Ridge",      "train"),
    (20, 0, 2, "North Central",              "Northern Highlands", "train"),
    (5,  0, 3, "Congo-Nile Divide Central",  "Western Ridge",      "train"),
    (6,  0, 4, "Congo-Nile Divide South",    "Western Ridge",      "eval"),
    (21, 0, 5, "Northeast Highlands West",   "Northern Highlands", "train"),
    (22, 0, 6, "Northeast Highlands East",   "Northern Highlands", "train"),

    # ── Row 1 ─────────────────────────────────────────────────────
    (1,  1, 0, "Virunga Foothills North",    "Northern Highlands", "train"),
    (23, 1, 1, "Virunga Foothills South",    "Northern Highlands", "train"),
    (24, 1, 2, "North Central East",         "Central Plateau",    "train"),
    (10, 1, 3, "Kivu Belt North",            "Kivu Belt",          "train"),
    (12, 1, 4, "Kivu Belt Southwest",        "Kivu Belt",          "eval"),
    (25, 1, 5, "Kivu Belt Far North",        "Kivu Belt",          "train"),
    (26, 1, 6, "Eastern Rift North",         "Eastern Savanna",    "train"),

    # ── Row 2 ─────────────────────────────────────────────────────
    (27, 2, 0, "Western Ridge North",        "Western Ridge",      "train"),
    (7,  2, 1, "Central Plateau NW",         "Central Plateau",    "train"),
    (28, 2, 2, "Central Plateau Mid",        "Central Plateau",    "train"),
    (29, 2, 3, "Central East Mid",           "Central Plateau",    "train"),
    (11, 2, 4, "Kivu Belt South",            "Kivu Belt",          "train"),
    (16, 2, 5, "Southern Valley West",       "Southern Valley",    "train"),
    (30, 2, 6, "Eastern Rift Central",       "Eastern Savanna",    "train"),

    # ── Row 3 ─────────────────────────────────────────────────────
    (2,  3, 0, "Northern Plateau West",      "Northern Highlands", "train"),
    (31, 3, 1, "Western Central",            "Western Ridge",      "train"),
    (8,  3, 2, "Central Plateau Core",       "Central Plateau",    "train"),
    (32, 3, 3, "Central Plateau South",      "Central Plateau",    "train"),
    (33, 3, 4, "Kivu Belt Central",          "Kivu Belt",          "train"),
    (34, 3, 5, "Southern Kivu",              "Kivu Belt",          "train"),
    (35, 3, 6, "Eastern Rift South",         "Eastern Savanna",    "train"),

    # ── Row 4 ─────────────────────────────────────────────────────
    (36, 4, 0, "Southwest Highlands",        "Western Ridge",      "train"),
    (37, 4, 1, "South Central West",         "Southern Valley",    "train"),
    (9,  4, 2, "Central Plateau East",       "Central Plateau",    "eval"),
    (38, 4, 3, "South Central",              "Eastern Savanna",    "train"),
    (39, 4, 4, "South Kivu Belt",            "Kivu Belt",          "train"),
    (17, 4, 5, "Southern Valley Central",    "Southern Valley",    "train"),
    (40, 4, 6, "Far East South",             "Eastern Savanna",    "train"),

    # ── Row 5 (last valid row) ────────────────────────────────────
    (3,  5, 0, "Northern Plateau East",      "Northern Highlands", "eval"),
    (41, 5, 1, "Southern West",              "Southern Valley",    "train"),
    (13, 5, 2, "Eastern Savanna North",      "Eastern Savanna",    "train"),
    (14, 5, 3, "Eastern Savanna Central",    "Eastern Savanna",    "train"),
    (15, 5, 4, "Eastern Savanna South",      "Eastern Savanna",    "eval"),
    (18, 5, 5, "Southern Valley East",       "Southern Valley",    "eval"),
    (42, 5, 6, "Far Southeast",              "Eastern Savanna",    "train"),
]

TRAIN_ZONE_IDS = [z[0] for z in ZONE_DEFINITIONS if z[5] == "train"]
EVAL_ZONE_IDS  = [z[0] for z in ZONE_DEFINITIONS if z[5] == "eval"]

# ── Reward weights ────────────────────────────────────────────────
REWARD = {
    "w_soil":          ZONE_SUITABILITY_WEIGHTS["soil"],
    "w_rain":          ZONE_SUITABILITY_WEIGHTS["rain"],
    "w_slope":         ZONE_SUITABILITY_WEIGHTS["slope"],
    # Reward audit finding: w_soil + w_rain + w_suitable_bonus can total
    # roughly 9.0 for a genuinely good cell (up to 3.0 + 2.0 + 4.0), all
    # computed purely from that cell's static quality with zero decay for
    # repeat visits. w_spacing was the only broad, radius-based counter-
    # force (MIN_SEED_SPACING covers a real neighbourhood, not just an
    # exact-cell match), but at 0.8 it was nowhere near competitive -- a
    # policy camping on one known-good cell still netted roughly +6 to +7
    # reward per repeat visit after every existing penalty. This is the
    # real, structural reason the redundant-placement nudge added
    # alongside w_new_coverage_bonus (0.5) barely moved seeding efficiency
    # across two full runs: it was never close to the right scale to
    # compete with what it was actually up against. Raised to be a
    # genuinely competitive fraction of the dominant placement reward,
    # not a token tax on it.
    "w_spacing":       3.5,
    "w_protected":    10.0,
    "w_disturbance":   0.6,
    "w_germ":          4.0,
    "w_diversity":     0.5,
    "w_reseed":        3.0,
    # w_reseed_shaping was removed here. It rewarded reducing distance to a
    # reseed target every step, meant to teach the policy to navigate there.
    # Navigation is now scripted (env/rwanda_env.py), so that job is
    # already done -- but the reward kept paying out in full on every
    # guaranteed-success approach, turning reseeding into an unusually
    # reliable, repeatable reward loop. Real data showed the cost: reseed
    # outcomes went from 0 to over 5,800 across a run, reward rose to among
    # the best all session, and pct_suitable_seeded dropped 6 to 9x at the
    # same time -- consistent with the policy spending its limited seed
    # budget cycling back to known targets instead of covering new ground.
    # w_reseed alone still rewards actually completing a correction once;
    # it no longer also pays for a walk that isn't being learned anymore.
    # Direct bonus for clearing the exact discrete threshold pct_suitable_seeded
    # measures (rain >= species.rain_min AND soil >= ZONE_MIN_SOIL AND not
    # no-plant/protected). Before this, the placement reward was a smooth
    # soil+rain-slope score with no term tied to that specific threshold --
    # a policy could earn decent reward from "pretty good" cells that never
    # actually cleared the bar the evaluation metric checks. This closes
    # that gap between what's trained and what's measured. Scaled comparable
    # to w_germ (4.0), not disproportionate to everything else the way
    # bad_abort used to be (see below).
    "w_suitable_bonus": 4.0,
    # Secondary to w_spacing above, which is now the primary anti-clustering
    # force and already covers this exact-cell case (distance 0 is always
    # within MIN_SEED_SPACING). This pair adds a smaller, additional signal
    # specifically for placing on a cell already seeded before, distinct
    # from merely placing too close to one -- both still skipped entirely
    # for a legitimate reseed, since revisiting a known failure is
    # deliberate correction, not wasteful redundancy (see env/rwanda_env.py).
    "w_new_coverage_bonus":  0.5,   # placing on a cell not seeded before
    "w_redundant_penalty":  -0.5,   # placing on a cell already seeded
    "step_penalty":    0.4,
    "battery_save":    1.0,
    # Was -200.0 -- 20-2000x larger than every other weight in this dict
    # (all in the 0.1-10 range). A single bad abort could erase the
    # equivalent of ~50 well-placed suitable seeds, which likely made the
    # policy extremely reluctant to ever abort, even on genuinely bad
    # zones where aborting is the correct call (this is the same abort
    # decision validated as real, learned behaviour in CHANGES section 1 --
    # a reward this lopsided risks the policy never actually using it).
    # Rescaled to be clearly the worst single-event penalty in the reward
    # function (bigger than w_protected=10, the next largest), without
    # being disproportionate enough to suppress the action outright.
    "bad_abort":       -20.0,
    "battery_empty":  -5.0,
    "obstacle_clear":  0.5,
    "obstacle_hit":   -3.0,
    "cover_correct":   0.0,
    "cover_wrong":    -0.1,
}
DISTURBANCE_BASE_PROB = 0.30

# ── Training ──────────────────────────────────────────────────────
TOTAL_TIMESTEPS = 200_000
N_ENVS          = 2
EVAL_FREQ       = 5_000
N_EVAL_EPISODES = 50
DISCOUNT_GAMMA  = 0.99

# ── Evaluation metrics ────────────────────────────────────────────
PRIMARY_METRIC = "pct_suitable_seeded"
EVAL_METRICS = [
    "pct_suitable_seeded",
    "seeding_ceiling",
    "seeding_efficiency",
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