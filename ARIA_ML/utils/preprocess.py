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

import os, sys
import numpy as np
import pandas as pd
import rasterio
import geopandas as gpd
from rasterio.warp import reproject, Resampling
from rasterio.transform import from_bounds
from scipy.ndimage import distance_transform_edt

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from configs.config import (
    RWANDA_BOUNDS, GRID_COLS, GRID_ROWS, RESOLUTION_M,
    DEM_PATH, SOIL_CLAY_PATH, SOIL_PH_PATH,
    SOIL_SAND_PATH, SOIL_SOC_PATH,
    RAINFALL_DIR, RAINFALL_FILES, LANDCOVER_DIR,
    WDPA_PATH, SPECIES_PATH, DATA_PROC_DIR,
    MAX_SLOPE_DEG, SPECIES, N_CHANNELS
)

REF_TRANSFORM = from_bounds(
    RWANDA_BOUNDS["left"], RWANDA_BOUNDS["bottom"],
    RWANDA_BOUNDS["right"], RWANDA_BOUNDS["top"],
    GRID_COLS, GRID_ROWS
)


def resample(path, method=Resampling.bilinear):
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


def norm(arr, lo=None, hi=None):
    lo = lo if lo is not None else np.nanmin(arr)
    hi = hi if hi is not None else np.nanmax(arr)
    if hi == lo:
        return np.zeros_like(arr, dtype=np.float32)
    return np.clip((arr - lo) / (hi - lo), 0.0, 1.0).astype(np.float32)


def sanitise(arr):
    return np.nan_to_num(
        arr.astype(np.float32), nan=0.0, posinf=1.0, neginf=0.0
    )


def compute_slope(elev_norm):
    elev_m = elev_norm * (4440 - 858) + 858
    dy, dx = np.gradient(elev_m, RESOLUTION_M, RESOLUTION_M)
    return np.degrees(np.arctan(np.sqrt(dx**2 + dy**2))).astype(np.float32)


def compute_soil(clay, ph, sand, soc):
    ph_r = ph * 140
    ph_s = np.clip(1.0 - np.abs(ph_r - 65) / 65, 0.0, 1.0)
    return (0.40*soc + 0.25*ph_s + 0.20*clay + 0.15*(1-sand)).astype(np.float32)


def encode_lc(lc):
    m = {10:0.3,20:0.8,30:0.9,40:0.5,50:0.0,
         60:0.7,70:0.0,80:0.0,90:0.2,95:0.1,100:0.4}
    out = np.zeros_like(lc, dtype=np.float32)
    for cls, v in m.items():
        out[lc == cls] = v
    return out


def compute_disturbance():
    from rasterio.features import rasterize
    from shapely.geometry import mapping
    gdf    = gpd.read_file(WDPA_PATH)
    shapes = [(mapping(g), 1) for g in gdf.geometry if g]
    mask   = rasterize(shapes, (GRID_ROWS, GRID_COLS),
                       transform=REF_TRANSFORM, fill=0, dtype=np.uint8)
    dist   = distance_transform_edt(1 - mask)
    prox   = 1.0 / (1.0 + dist / 20.0)
    prox[mask == 1] = 1.0
    return prox.astype(np.float32)


def compute_obstacle(slope_deg, elev_norm):
    """
    Obstacle map: combines steep terrain + sudden elevation spikes.
    Values close to 1.0 = obstacle present.
    """
    slope_obs  = (slope_deg > MAX_SLOPE_DEG).astype(np.float32)
    # Elevation variance in 3x3 neighbourhood as turbulence proxy
    from scipy.ndimage import uniform_filter
    elev_local = uniform_filter(elev_norm, size=3)
    turb       = np.abs(elev_norm - elev_local)
    turb_norm  = norm(turb)
    obstacle   = np.clip(slope_obs * 0.7 + turb_norm * 0.3, 0.0, 1.0)
    return obstacle.astype(np.float32)


def run():
    os.makedirs(DATA_PROC_DIR, exist_ok=True)
    print("ARIA — Preprocessing Pipeline")
    print("=" * 50)

    print("[1/9] Loading DEM...")
    elev_raw  = resample(DEM_PATH)
    elevation = norm(elev_raw, 858, 4440)

    print("[2/9] Computing slope...")
    slope_deg  = compute_slope(elevation)
    slope_norm = norm(slope_deg, 0, 90)
    np.save(os.path.join(DATA_PROC_DIR, "slope_grid.npy"), slope_deg)

    print("[3/9] Loading soil layers...")
    clay = norm(resample(SOIL_CLAY_PATH))
    ph   = norm(resample(SOIL_PH_PATH))
    sand = norm(resample(SOIL_SAND_PATH))
    soc  = norm(resample(SOIL_SOC_PATH))
    soil = compute_soil(clay, ph, sand, soc)

    print("[4/9] Loading land cover...")
    lc_files = sorted([
        os.path.join(LANDCOVER_DIR, f)
        for f in os.listdir(LANDCOVER_DIR) if f.endswith(".tif")
    ])
    lcs = [resample(f, Resampling.nearest) for f in lc_files]
    lc  = encode_lc(np.nanmax(np.stack(lcs, 0), 0))

    print("[5/9] Loading rainfall stack...")
    stack = []
    for fn in RAINFALL_FILES:
        r = resample(os.path.join(RAINFALL_DIR, fn))
        r = np.where(r < 0, 0.0, r)
        stack.append(r)
    rain_stack = np.stack(stack, 0)
    gmax = np.nanmax(rain_stack)
    if gmax > 0:
        rain_stack /= gmax
    rain_stack = sanitise(rain_stack)
    np.save(os.path.join(DATA_PROC_DIR, "rainfall_stack.npy"),
            rain_stack.astype(np.float32))

    print("[6/9] Computing disturbance map...")
    dist_map = sanitise(compute_disturbance())
    np.save(os.path.join(DATA_PROC_DIR, "disturbance_map.npy"), dist_map)

    print("[7/9] Computing obstacle map...")
    obs_map = sanitise(compute_obstacle(slope_deg, elevation))
    np.save(os.path.join(DATA_PROC_DIR, "obstacle_map.npy"), obs_map)

    print("[8/9] Building terrain grid (ROWS, COLS, 5)...")
    grid = np.stack([
        sanitise(elevation),
        sanitise(slope_norm),
        sanitise(soil),
        sanitise(rain_stack[0]),
        sanitise(lc),
    ], axis=-1).astype(np.float32)
    np.save(os.path.join(DATA_PROC_DIR, "terrain_grid.npy"), grid)

    print("[8b/9] Building no-plant mask...")
    no_plant = slope_deg > MAX_SLOPE_DEG
    np.save(os.path.join(DATA_PROC_DIR, "no_plant_mask.npy"), no_plant)
    print(f"       {no_plant.mean()*100:.1f}% masked")

    print("[9/9] Building species table...")
    rows = [{"species_id": k, "name": v["name"],
             "germ_steps": v["germ_steps"],
             "mature_steps": v["mature_steps"],
             "rain_min": v["rain_min"]}
            for k, v in SPECIES.items()]
    pd.DataFrame(rows).to_csv(
        os.path.join(DATA_PROC_DIR, "species_table.csv"), index=False
    )

    print("\n" + "="*50)
    print("Preprocessing complete.")
    print(f"  terrain_grid.npy    {grid.shape}")
    print(f"  rainfall_stack.npy  {rain_stack.shape}")
    print(f"  disturbance_map.npy {dist_map.shape}")
    print(f"  obstacle_map.npy    {obs_map.shape}")
    print(f"  no_plant_mask.npy   {no_plant.shape}")


if __name__ == "__main__":
    run()
