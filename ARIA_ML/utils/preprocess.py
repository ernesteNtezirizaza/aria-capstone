"""
utils/preprocess.py
===================
Loads all six Rwanda datasets and produces clean numpy grids.

Outputs saved to data/processed/:
  terrain_grid.npy    (GRID_ROWS, GRID_COLS, 5)
  slope_grid.npy      (GRID_ROWS, GRID_COLS)
  disturbance_map.npy (GRID_ROWS, GRID_COLS)
  obstacle_map.npy    (GRID_ROWS, GRID_COLS)
  rainfall_stack.npy  (6, GRID_ROWS, GRID_COLS)
  no_plant_mask.npy   (GRID_ROWS, GRID_COLS) bool
  species_table.csv

Run: python utils/preprocess.py
"""

import glob
import logging
import os
import sys

import geopandas as gpd
import numpy as np
import pandas as pd
import rasterio
from rasterio.features import rasterize
from rasterio.transform import from_bounds
from rasterio.warp import Resampling, reproject
from scipy.ndimage import distance_transform_edt, uniform_filter
from shapely.geometry import mapping

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from configs.config import (
    DATA_PROC_DIR, DEM_PATH, GRID_COLS, GRID_ROWS, LANDCOVER_DIR,
    MAX_SLOPE_DEG, N_CHANNELS, RAINFALL_DIR, RAINFALL_FILES, RESOLUTION_M,
    RWANDA_BOUNDS, SOIL_DIR, SPECIES, SPECIES_PATH, WDPA_PATH, WDPA_PATHS,
)

logging.basicConfig(level=logging.INFO, format="%(message)s")
log = logging.getLogger("preprocess")

REF_TRANSFORM = from_bounds(
    RWANDA_BOUNDS["left"], RWANDA_BOUNDS["bottom"],
    RWANDA_BOUNDS["right"], RWANDA_BOUNDS["top"],
    GRID_COLS, GRID_ROWS,
)

# ── Named constants -----------------------------------------------------
# ESA WorldCover class codes -> a rough [0,1] "how plantable is this
# land cover" score. 0 = unsuitable/already built or water, 1 = ideal
# open/grass/cropland for new tree planting. Codes match the ESA
# WorldCover 10m v200 legend (https://esa-worldcover.org).
LANDCOVER_SUITABILITY = {
    10: 0.3,   # Tree cover        -- already forested, lower priority
    20: 0.8,   # Shrubland         -- good candidate
    30: 0.9,   # Grassland         -- best open-ground candidate
    40: 0.5,   # Cropland          -- moderate, may compete with agriculture
    50: 0.0,   # Built-up          -- unplantable
    60: 0.7,   # Bare/sparse veg   -- plantable but likely poor soil
    70: 0.0,   # Snow/ice          -- unplantable (not expected in Rwanda)
    80: 0.0,   # Permanent water   -- unplantable
    90: 0.2,   # Herbaceous wetland-- plantable only with caution
    95: 0.1,   # Mangroves         -- specialised, not a general target
    100: 0.4,  # Moss/lichen       -- marginal
}

# compute_soil(): the original 4 core soil variables keep their
# domain-informed relative weights (soil organic carbon matters most
# for fertility, then pH, then texture), scaled to sum to 0.85 instead
# of 1.0 so any additional discovered layers (nitrogen, carbon, ...)
# can share the remaining 0.15 -- see compute_soil()'s docstring for
# the full reasoning.
SOIL_CORE_WEIGHTS = {"soc": 0.40, "ph": 0.25, "clay": 0.20, "sand": 0.15}
SOIL_EXTRA_LAYER_BUDGET = 0.15  # total weight shared by any non-core layers found

# SoilGrids stores pH as pH x10 (e.g. 65 = pH 6.5); after normalisation
# to [0,1] via norm(), a pH of 6.5 (the middle of the ideal 6.0-7.0
# range most crops/trees tolerate) sits at approximately 0.65. Distance
# from this ideal, not the raw value, is what predicts fertility --
# both very acidic and very alkaline soil are worse than neutral.
SOIL_IDEAL_PH_NORMALISED = 0.65

# compute_disturbance(): converts distance-from-protected-area (metres)
# into a proximity score via 1 / (1 + distance / DECAY_METRES). This
# constant sets how quickly protection "fades" with distance -- at
# DECAY_METRES away, proximity has already dropped to 0.5; by 5x that
# distance it is under 0.2. 20m keeps the effect tightly local to
# actual reserve boundaries rather than influencing placement decisions
# kilometres away.
DISTURBANCE_DECAY_METRES = 20.0

# compute_obstacle(): blend weight between "terrain is too steep to
# safely fly/land" (a hard, well-defined threshold) and "local
# elevation is turbulent" (a softer proxy for unpredictable air/ground
# conditions near ridges and cliffs). Weighted toward slope because
# slope is a direct, physically-grounded hazard signal; turbulence is
# a secondary indicator layered on top of it, not an equal partner.
OBSTACLE_SLOPE_WEIGHT = 0.7
OBSTACLE_TURBULENCE_WEIGHT = 0.3


def resample(path, method=Resampling.bilinear):
    """
    Reprojects a single-band raster onto the shared Rwanda reference
    grid (REF_TRANSFORM, EPSG:4326, GRID_ROWS x GRID_COLS), so every
    dataset in this pipeline ends up on identical, directly comparable
    pixel positions regardless of its original resolution or extent.
    Source nodata becomes NaN in the output, not 0, so downstream code
    can distinguish "no data here" from "a real value of zero".
    """
    with rasterio.open(path) as src:
        dst = np.zeros((GRID_ROWS, GRID_COLS), dtype=np.float32)
        reproject(
            source=rasterio.band(src, 1),
            destination=dst,
            src_transform=src.transform,
            src_crs=src.crs,
            dst_transform=REF_TRANSFORM,
            dst_crs="EPSG:4326",
            resampling=method,
            src_nodata=src.nodata,
            dst_nodata=np.nan,
        )
    return dst


def norm(arr, value_min=None, value_max=None, name="array"):
    """
    Min-max normalises arr to [0,1], clipping to that range. If
    value_min/value_max aren't given, they're taken from the array's
    own finite values (np.nanmin/np.nanmax). Raises a clear, actionable
    error if the array is entirely non-finite, and warns (rather than
    silently proceeding) if more than half of it is -- both are usually
    a sign of a CRS/extent mismatch upstream, not a healthy dataset.
    """
    valid = np.isfinite(arr)
    n_valid = int(valid.sum())
    if n_valid == 0:
        raise ValueError(
            f"norm(): '{name}' is entirely NaN/inf ({arr.size} cells, "
            f"0 valid). If this array came straight from resample(), "
            f"check the source raster's CRS/extent against "
            f"RWANDA_BOUNDS in config.py. If this array is DERIVED "
            f"(e.g. via scipy.ndimage filters on another array), check "
            f"that the upstream array was sanitise()'d first — NaN "
            f"propagates through filters like uniform_filter and can "
            f"poison the whole array even from a few edge/nodata cells."
        )
    if n_valid < arr.size:
        pct_nan = 100 * (1 - n_valid / arr.size)
        if pct_nan > 50:
            log.warning(f"  WARNING: '{name}' is {pct_nan:.1f}% NaN — "
                        f"check source raster extent/CRS, or sanitise() "
                        f"upstream inputs before any spatial filtering")
    value_min = value_min if value_min is not None else np.nanmin(arr)
    value_max = value_max if value_max is not None else np.nanmax(arr)
    if value_max == value_min:
        return np.zeros_like(arr, dtype=np.float32)
    return np.clip((arr - value_min) / (value_max - value_min), 0.0, 1.0).astype(np.float32)


def sanitise(arr):
    """Replaces NaN with 0.0 and +/-inf with 1.0/0.0, for arrays that
    must not carry NaN forward (e.g. right before np.save or before
    feeding into a spatial filter, where NaN would otherwise spread to
    every neighbouring cell it touches)."""
    return np.nan_to_num(
        arr.astype(np.float32), nan=0.0, posinf=1.0, neginf=0.0
    )


def compute_slope(elev_norm, elev_min, elev_max):
    """Convert normalised elevation back to metres using actual DEM range,
    then compute slope in degrees from the real-world elevation gradient."""
    elev_m = elev_norm * (elev_max - elev_min) + elev_min
    dy, dx = np.gradient(elev_m, RESOLUTION_M, RESOLUTION_M)
    return np.degrees(np.arctan(np.sqrt(dx**2 + dy**2))).astype(np.float32)


def compute_soil(layers: dict) -> np.ndarray:
    """
    layers: dict mapping soil variable name -> normalised [0,1] array,
            e.g. {"clay": ..., "ph": ..., "sand": ..., "soc": ...,
                   "nitrogen": ..., "carbon": ...}. Keys beyond clay/ph/
            sand/soc are discovered dynamically from whatever
            rwanda_soil_*.tif files exist in data/raw/soil/.

    Combines the 4 core variables using SOIL_CORE_WEIGHTS (see module
    constants above), plus an equal share of SOIL_EXTRA_LAYER_BUDGET for
    any additional discovered layers. pH is scored by closeness to
    SOIL_IDEAL_PH_NORMALISED rather than used directly, since both very
    acidic and very alkaline soil are worse than neutral.
    """
    core_scale = (1.0 - SOIL_EXTRA_LAYER_BUDGET) / sum(SOIL_CORE_WEIGHTS.values())

    ph_score = np.clip(
        1.0 - np.abs(layers["ph"] - SOIL_IDEAL_PH_NORMALISED) / SOIL_IDEAL_PH_NORMALISED,
        0.0, 1.0,
    )
    composite = (
        SOIL_CORE_WEIGHTS["soc"] * core_scale * layers["soc"]
        + SOIL_CORE_WEIGHTS["ph"] * core_scale * ph_score
        + SOIL_CORE_WEIGHTS["clay"] * core_scale * layers["clay"]
        + SOIL_CORE_WEIGHTS["sand"] * core_scale * (1.0 - layers["sand"])
    )

    extra_keys = [k for k in layers if k not in ("clay", "ph", "sand", "soc")]
    if extra_keys:
        extra_weight_each = SOIL_EXTRA_LAYER_BUDGET / len(extra_keys)
        for key in extra_keys:
            composite = composite + extra_weight_each * layers[key]

    return composite.astype(np.float32)


def encode_lc(landcover_class: np.ndarray) -> np.ndarray:
    """Maps each cell's ESA WorldCover class code to a [0,1] plantability
    score via LANDCOVER_SUITABILITY (see module constants above). Any
    class code not in the mapping scores 0.0 (treated as unplantable)."""
    scores = np.zeros_like(landcover_class, dtype=np.float32)
    for class_code, suitability in LANDCOVER_SUITABILITY.items():
        scores[landcover_class == class_code] = suitability
    return scores


def compute_disturbance() -> np.ndarray:
    """
    Builds a proximity-to-protected-area score: 1.0 inside a protected
    area, decaying toward 0 with distance outside it (see
    DISTURBANCE_DECAY_METRES above). Reads every shapefile in
    WDPA_PATHS (Rwanda's protected-area boundaries may be split across
    several WDPA export files) and merges them into one mask before
    computing distance.
    """
    protected_area_gdfs = []
    for wdpa_path in WDPA_PATHS:
        try:
            protected_area_gdfs.append(gpd.read_file(wdpa_path))
        except Exception as load_error:
            log.warning(f"  Warning: {wdpa_path}: {load_error}")

    if protected_area_gdfs:
        combined = gpd.GeoDataFrame(
            pd.concat(protected_area_gdfs, ignore_index=True),
            crs=protected_area_gdfs[0].crs,
        )
    else:
        combined = gpd.read_file(WDPA_PATH)

    shapes = [(mapping(geom), 1) for geom in combined.geometry if geom]
    protected_mask = rasterize(
        shapes, (GRID_ROWS, GRID_COLS),
        transform=REF_TRANSFORM, fill=0, dtype=np.uint8,
    )
    distance_m = distance_transform_edt(1 - protected_mask)
    proximity = 1.0 / (1.0 + distance_m / DISTURBANCE_DECAY_METRES)
    proximity[protected_mask == 1] = 1.0
    return proximity.astype(np.float32)


def compute_obstacle(slope_deg, elev_norm):
    """
    Obstacle map: combines steep terrain + sudden elevation spikes.
    Values close to 1.0 = obstacle present.
    """
    slope_obstacle = (slope_deg > MAX_SLOPE_DEG).astype(np.float32)
    # Elevation variance in a 3x3 neighbourhood as a turbulence proxy.
    elev_clean = sanitise(elev_norm)
    elev_local_mean = uniform_filter(elev_clean, size=3)
    turbulence = np.abs(elev_clean - elev_local_mean)
    turbulence_norm = norm(turbulence, name="turbulence")
    obstacle = np.clip(
        slope_obstacle * OBSTACLE_SLOPE_WEIGHT
        + turbulence_norm * OBSTACLE_TURBULENCE_WEIGHT,
        0.0, 1.0,
    )
    return obstacle.astype(np.float32)


# ── Pipeline steps --------------------------------------------------------
# Each step function does exactly one thing and returns exactly what
# later steps or run()'s final summary need, instead of one long
# procedural script mixing I/O, computation, and logging together.

def _load_elevation():
    """Loads and normalises the DEM. Returns (elevation_norm, elev_min,
    elev_max, raw_elevation) -- elev_min/max are derived from the actual
    DEM data (ignoring nodata zeros), never hardcoded."""
    elev_raw = resample(DEM_PATH)
    elev_min = float(np.nanmin(elev_raw[elev_raw > 0]))  # ignore nodata zeros
    elev_max = float(np.nanmax(elev_raw))
    log.info(f"       Elevation range: {elev_min:.0f}m - {elev_max:.0f}m (from DEM)")
    elevation = norm(elev_raw, elev_min, elev_max, name="elevation")
    return elevation, elev_min, elev_max


def _load_soil():
    """
    Discovers every rwanda_soil_*.tif in SOIL_DIR, excludes nitrogen
    entirely (see the inline comment below) and any other layer that's
    mostly invalid after clipping to Rwanda's bounds, then builds the
    soil composite via compute_soil(). Returns the composite array.
    """
    soil_paths = sorted(glob.glob(os.path.join(SOIL_DIR, "rwanda_soil_*.tif")))
    # Nitrogen excluded here at discovery, not later at composite-build
    # time -- it's 70.9% NaN/invalid after clipping to Rwanda bounds
    # (CRS/extent mismatch against the other soil layers) and should
    # never be loaded, computed, or referenced anywhere in the project,
    # not just skipped from the composite.
    soil_paths = [p for p in soil_paths if "nitrogen" not in os.path.basename(p).lower()]
    if not soil_paths:
        raise FileNotFoundError(f"No rwanda_soil_*.tif files found in {SOIL_DIR}")
    log.info(f"  Found {len(soil_paths)} soil layers: "
             f"{[os.path.basename(p) for p in soil_paths]}")

    soil_layers = {}
    excluded_layers = []
    for path in soil_paths:
        # "rwanda_soil_clay.tif" -> "clay", "rwanda_soil_nitrogen.tif" -> "nitrogen"
        key = os.path.basename(path).replace("rwanda_soil_", "").replace(".tif", "")
        raw = resample(path)
        valid_frac = np.isfinite(raw).mean()
        if valid_frac < 0.5 and key not in ("clay", "ph", "sand", "soc"):
            # compute_soil() combines extra layers (nitrogen, carbon, ...)
            # via plain addition, not nanmean -- it does NOT skip NaN
            # per-pixel. A layer this sparse doesn't just "contribute
            # less", it propagates NaN through the sum everywhere it's
            # missing, and sanitise() later converts that NaN to 0.0 --
            # the WORST possible soil score, not "no data". Excluding
            # the bad layer here, before compute_soil() ever sees it,
            # is a correctness fix, not just a cleaner warning.
            log.info(f"  EXCLUDED soil_{key} from composite "
                      f"({100*(1-valid_frac):.1f}% invalid)")
            excluded_layers.append(key)
            continue
        soil_layers[key] = norm(raw, name=f"soil_{key}")

    if excluded_layers:
        log.info(f"  Soil composite built from {len(soil_layers)} layers "
                  f"(excluded: {excluded_layers})")
    for required in ("clay", "ph", "sand", "soc"):
        if required not in soil_layers:
            raise ValueError(
                f"compute_soil() requires a '{required}' layer "
                f"(rwanda_soil_{required}.tif) but it wasn't found in {SOIL_DIR}"
            )
    return compute_soil(soil_layers)


def _load_landcover():
    """Loads every landcover tile in LANDCOVER_DIR, takes the per-cell
    max across tiles (Rwanda spans multiple ESA WorldCover tiles), and
    encodes the result to a [0,1] plantability score."""
    lc_files = sorted([
        os.path.join(LANDCOVER_DIR, f)
        for f in os.listdir(LANDCOVER_DIR) if f.endswith(".tif")
    ])
    lc_tiles = [resample(f, Resampling.nearest) for f in lc_files]
    landcover_class = np.nanmax(np.stack(lc_tiles, 0), 0)
    return encode_lc(landcover_class)


def _load_rainfall():
    """Loads and stacks every file in RAINFALL_FILES, clips negative
    values to 0, and normalises the whole stack by its own global max
    so all months share one consistent [0,1] scale. Returns the
    sanitised (N_SEASONS, GRID_ROWS, GRID_COLS) stack."""
    monthly_grids = []
    for filename in RAINFALL_FILES:
        rainfall = resample(os.path.join(RAINFALL_DIR, filename))
        rainfall = np.where(rainfall < 0, 0.0, rainfall)
        monthly_grids.append(rainfall)
    rain_stack = np.stack(monthly_grids, 0)
    rain_global_max = np.nanmax(rain_stack)
    if rain_global_max > 0:
        rain_stack /= rain_global_max
    return sanitise(rain_stack)


def _build_species_table():
    """Builds the species lookup table (one row per SPECIES entry) as a
    DataFrame ready to write to species_table.csv."""
    rows = [
        {
            "species_id": species_id,
            "name": species["name"],
            "germ_steps": species["germ_steps"],
            "mature_steps": species["mature_steps"],
            "rain_min": species["rain_min"],
        }
        for species_id, species in SPECIES.items()
    ]
    return pd.DataFrame(rows)


def run():
    """
    Runs the full preprocessing pipeline end to end: DEM -> slope ->
    soil -> landcover -> rainfall -> disturbance -> obstacles -> the
    stacked terrain grid, no-plant mask, and species table, saving each
    output to DATA_PROC_DIR as it's produced.
    """
    os.makedirs(DATA_PROC_DIR, exist_ok=True)
    log.info("ARIA — Preprocessing Pipeline")
    log.info("=" * 50)

    steps = [
        "Loading DEM",
        "Computing slope",
        "Loading soil layers",
        "Loading land cover",
        "Loading rainfall stack",
        "Computing disturbance map",
        "Computing obstacle map",
        "Building terrain grid (ROWS, COLS, 5)",
        "Building no-plant mask",
        "Building species table",
    ]
    step_iter = iter(enumerate(steps, start=1))

    def log_step(name=None):
        i, default_name = next(step_iter)
        log.info(f"[{i}/{len(steps)}] {name or default_name}...")

    log_step()
    elevation, elev_min, elev_max = _load_elevation()

    log_step()
    slope_deg = compute_slope(elevation, elev_min, elev_max)
    slope_norm = norm(slope_deg, 0, 90, name="slope")
    np.save(os.path.join(DATA_PROC_DIR, "slope_grid.npy"), slope_deg)

    log_step()
    soil = _load_soil()

    log_step()
    landcover = _load_landcover()

    log_step()
    rain_stack = _load_rainfall()
    np.save(os.path.join(DATA_PROC_DIR, "rainfall_stack.npy"), rain_stack.astype(np.float32))

    log_step()
    disturbance_map = sanitise(compute_disturbance())
    np.save(os.path.join(DATA_PROC_DIR, "disturbance_map.npy"), disturbance_map)

    log_step()
    obstacle_map = sanitise(compute_obstacle(slope_deg, elevation))
    np.save(os.path.join(DATA_PROC_DIR, "obstacle_map.npy"), obstacle_map)

    log_step()
    terrain_grid = np.stack([
        sanitise(elevation),
        sanitise(slope_norm),
        sanitise(soil),
        sanitise(rain_stack[0]),
        sanitise(landcover),
    ], axis=-1).astype(np.float32)
    np.save(os.path.join(DATA_PROC_DIR, "terrain_grid.npy"), terrain_grid)

    log_step()
    no_plant_mask = slope_deg > MAX_SLOPE_DEG
    np.save(os.path.join(DATA_PROC_DIR, "no_plant_mask.npy"), no_plant_mask)
    log.info(f"       {no_plant_mask.mean()*100:.1f}% masked")

    log_step()
    species_table = _build_species_table()
    species_table.to_csv(os.path.join(DATA_PROC_DIR, "species_table.csv"), index=False)

    log.info("\n" + "=" * 50)
    log.info("Preprocessing complete.")
    log.info(f"  terrain_grid.npy    {terrain_grid.shape}")
    log.info(f"  rainfall_stack.npy  {rain_stack.shape}")
    log.info(f"  disturbance_map.npy {disturbance_map.shape}")
    log.info(f"  obstacle_map.npy    {obstacle_map.shape}")
    log.info(f"  no_plant_mask.npy   {no_plant_mask.shape}")


if __name__ == "__main__":
    run()
