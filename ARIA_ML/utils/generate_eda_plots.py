"""
utils/generate_eda_plots.py
============================
Exploratory data analysis diagrams for ARIA's raw ecological dataset.

Unlike a one-off notebook cell, this reads soil layers and rainfall
months the SAME way preprocess.py and config.py do -- via SOIL_DIR glob
and RAINFALL_FILES -- so it automatically reflects whatever real data is
actually present (e.g. nitrogen/carbon soil layers, a fuller rainfall
year) without needing to be edited every time a new file is added.

Produces, in RESULTS_DIR/eda/:
  1_ecological_layer_heatmaps.png   elevation, slope, soil composite, rainfall
  2_correlation_heatmap.png         Pearson correlation between the 4 variables
  3_pairwise_relationships.png      hexbin/scatter matrix
  4_distributions.png               histograms with mean lines
  5_composite_suitability_map.png   soil*3 + rain*2 - slope*1, same weights as
                                     configs.config.ZONE_SUITABILITY_WEIGHTS
  6_dataset_summary.png             which soil layers / rainfall months are
                                     actually present right now (a live report,
                                     not a fixed "before/after" snapshot)

Usage:
    python utils/generate_eda_plots.py
"""

import os
import sys
import glob

import numpy as np
import warnings
import matplotlib
import matplotlib.pyplot as plt
import seaborn as sns
import pandas as pd
import rasterio
from rasterio.warp import reproject, Resampling
from rasterio.transform import from_bounds

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from configs.config import (
    DEM_PATH, SOIL_DIR, RAINFALL_DIR, RAINFALL_FILES,
    RWANDA_BOUNDS, RESULTS_DIR, ZONE_SUITABILITY_WEIGHTS, MAX_SLOPE_DEG,
)

OUT_DIR = os.path.join(RESULTS_DIR, "eda")
os.makedirs(OUT_DIR, exist_ok=True)

plt.rcParams.update({
    "font.size": 13, "axes.titlesize": 15, "axes.labelsize": 13,
    "figure.dpi": 200, "savefig.dpi": 200, "axes.titleweight": "bold",
})

# A moderate common grid -- enough resolution for clear plots without the
# memory cost of reprojecting every layer at full native resolution.
COLS, ROWS = 500, 430
EXTENT = [RWANDA_BOUNDS["left"], RWANDA_BOUNDS["right"],
          RWANDA_BOUNDS["bottom"], RWANDA_BOUNDS["top"]]
DST_T = from_bounds(RWANDA_BOUNDS["left"], RWANDA_BOUNDS["bottom"],
                     RWANDA_BOUNDS["right"], RWANDA_BOUNDS["top"], COLS, ROWS)
DST_CRS = "EPSG:4326"


def reproj_to_grid(path, band=1, resampling=Resampling.bilinear):
    with rasterio.open(path) as src:
        dst = np.full((ROWS, COLS), np.nan, dtype=np.float32)
        reproject(
            source=rasterio.band(src, band), destination=dst,
            src_transform=src.transform, src_crs=src.crs,
            dst_transform=DST_T, dst_crs=DST_CRS, resampling=resampling,
            src_nodata=src.nodata, dst_nodata=np.nan,
        )
    return dst


def load_layers():
    print("Loading DEM...")
    dem = reproj_to_grid(DEM_PATH)
    dem[dem <= 0] = np.nan

    print("Computing slope...")
    res_m = (RWANDA_BOUNDS["right"] - RWANDA_BOUNDS["left"]) / COLS * 111320 * \
        np.cos(np.radians((RWANDA_BOUNDS["top"] + RWANDA_BOUNDS["bottom"]) / 2))
    dy, dx = np.gradient(np.nan_to_num(dem, nan=np.nanmean(dem)), res_m, res_m)
    slope_deg = np.degrees(np.arctan(np.sqrt(dx**2 + dy**2)))
    slope_deg[np.isnan(dem)] = np.nan

    print("Discovering soil layers (same glob as preprocess.py)...")
    soil_paths = sorted(glob.glob(os.path.join(SOIL_DIR, "rwanda_soil_*.tif")))
    # Nitrogen excluded at discovery -- see the matching comment in
    # utils/preprocess.py for why.
    soil_paths = [p for p in soil_paths if "nitrogen" not in os.path.basename(p).lower()]
    if not soil_paths:
        raise FileNotFoundError(f"No rwanda_soil_*.tif files found in {SOIL_DIR}")
    soil_names = [os.path.basename(p).replace("rwanda_soil_", "").replace(".tif", "")
                  for p in soil_paths]
    print(f"  Found {len(soil_paths)} soil layers: {soil_names}")
    soil_layers = []
    for p in soil_paths:
        layer = reproj_to_grid(p)
        layer[layer <= 0] = np.nan
        mn, mx = np.nanmin(layer), np.nanmax(layer)
        if mx > mn:
            layer = (layer - mn) / (mx - mn)
        soil_layers.append(layer)
    # Same expected edge case as config.py's derivation: pixels with no
    # valid data in ANY soil layer (water bodies) are legitimately NaN --
    # not a bug, suppressed deliberately rather than left unexplained.
    with warnings.catch_warnings():
        warnings.filterwarnings("ignore", message="Mean of empty slice")
        soil_composite = np.nanmean(np.stack(soil_layers), axis=0)

    print(f"Loading rainfall ({len(RAINFALL_FILES)} months from RAINFALL_FILES)...")
    rain_stack = []
    for fn in RAINFALL_FILES:
        path = os.path.join(RAINFALL_DIR, fn)
        if not os.path.exists(path):
            print(f"  WARNING: {fn} not found, skipping -- rainfall diagrams "
                  f"will be built from whichever months ARE present")
            continue
        r = reproj_to_grid(path)
        r[r < 0] = np.nan
        rain_stack.append(r)
    if not rain_stack:
        raise FileNotFoundError(
            "None of RAINFALL_FILES were found in raw/rainfall/ -- "
            "check configs/config.py's RAINFALL_FILES list matches what's on disk"
        )
    rain_stack = np.stack(rain_stack)
    rain_mean = np.nanmean(rain_stack, axis=0)
    rain_norm = rain_mean / np.nanmax(rain_mean)

    return dem, slope_deg, soil_composite, rain_norm, soil_names, len(rain_stack)


def plot_layer_heatmaps(elevation, slope, soil, rainfall):
    fig, axes = plt.subplots(2, 2, figsize=(15, 13))
    fig.suptitle("ARIA Ecological Input Layers — Rwanda", fontsize=19, fontweight="bold", y=0.99)
    layers = [
        (elevation, "Elevation (m)", "terrain", axes[0, 0]),
        (slope, "Slope (degrees)", "YlOrRd", axes[0, 1]),
        (soil, "Soil Suitability Composite (normalised)", "YlGn", axes[1, 0]),
        (rainfall, "Rainfall (normalised, CHIRPS mean)", "Blues", axes[1, 1]),
    ]
    for arr, title, cmap, ax in layers:
        im = ax.imshow(arr, cmap=cmap, extent=EXTENT, aspect="auto")
        ax.set_title(title, fontsize=14, pad=10)
        ax.set_xlabel("Longitude"); ax.set_ylabel("Latitude")
        fig.colorbar(im, ax=ax, fraction=0.046, pad=0.04)
    plt.tight_layout(rect=[0, 0, 1, 0.95])
    plt.savefig(os.path.join(OUT_DIR, "1_ecological_layer_heatmaps.png"), bbox_inches="tight")
    plt.show()
    plt.close()
    print("Saved 1_ecological_layer_heatmaps.png")


def plot_correlation_and_pairwise(elevation, slope, soil, rainfall):
    mask = ~(np.isnan(elevation) | np.isnan(slope) | np.isnan(soil) | np.isnan(rainfall))
    df = pd.DataFrame({"Elevation": elevation[mask], "Slope": slope[mask],
                        "Soil": soil[mask], "Rainfall": rainfall[mask]})
    df_s = df.sample(n=min(40000, len(df)), random_state=42)

    fig, ax = plt.subplots(figsize=(8.5, 7.5))
    sns.heatmap(df_s.corr(), annot=True, fmt=".2f", cmap="coolwarm", vmin=-1, vmax=1,
                square=True, linewidths=1.2, linecolor="white",
                annot_kws={"size": 15, "weight": "bold"}, ax=ax)
    ax.set_title("Correlation Between Ecological Variables", fontsize=16, fontweight="bold", pad=16)
    plt.tight_layout()
    plt.savefig(os.path.join(OUT_DIR, "2_correlation_heatmap.png"), bbox_inches="tight")
    plt.show()
    plt.close()
    print("Saved 2_correlation_heatmap.png")

    g = sns.PairGrid(df_s, diag_sharey=False, height=2.8)
    g.map_upper(sns.scatterplot, s=4, alpha=0.15, color="#2E7D32")
    g.map_lower(lambda x, y, **kw: plt.hexbin(x, y, gridsize=30, cmap="viridis", mincnt=1))
    g.map_diag(sns.histplot, color="#1B5E20", kde=True)
    g.fig.suptitle("Pairwise Relationships Between Ecological Variables", fontsize=17, fontweight="bold", y=1.02)
    g.savefig(os.path.join(OUT_DIR, "3_pairwise_relationships.png"), bbox_inches="tight")
    plt.show()
    plt.close()
    print("Saved 3_pairwise_relationships.png")

    fig, axes = plt.subplots(1, 4, figsize=(21, 5.5))
    fig.suptitle("Distribution of Ecological Variables Across Rwanda", fontsize=17, fontweight="bold")
    specs = [(elevation[mask], "Elevation (m)", "#6D4C41"), (slope[mask], "Slope (degrees)", "#EF6C00"),
             (soil[mask], "Soil Suitability (0-1)", "#2E7D32"), (rainfall[mask], "Rainfall (normalised)", "#1565C0")]
    for (arr, title, color), ax in zip(specs, axes):
        sns.histplot(arr, bins=50, color=color, ax=ax, kde=True)
        ax.axvline(np.mean(arr), color="black", linestyle="--", linewidth=1.8, label=f"mean={np.mean(arr):.2f}")
        ax.set_title(title, fontsize=13); ax.legend(fontsize=10)
    plt.tight_layout(rect=[0, 0, 1, 0.92])
    plt.savefig(os.path.join(OUT_DIR, "4_distributions.png"), bbox_inches="tight")
    plt.show()
    plt.close()
    print("Saved 4_distributions.png")


def plot_composite_suitability(soil, rainfall, slope):
    soil_n = (soil - np.nanmin(soil)) / (np.nanmax(soil) - np.nanmin(soil))
    slope_pen = np.clip(slope / 45.0, 0, 1)
    w = ZONE_SUITABILITY_WEIGHTS
    suitability = np.clip(
        (w["soil"] * soil_n + w["rain"] * rainfall - w["slope"] * slope_pen)
        / (w["soil"] + w["rain"] + w["slope"]), 0, 1
    )
    fig, ax = plt.subplots(figsize=(9.5, 8.5))
    im = ax.imshow(suitability, cmap="RdYlGn", extent=EXTENT, aspect="auto", vmin=0, vmax=1)
    ax.set_title(
        f"Composite Zone Suitability\n(soil ×{w['soil']} + rainfall ×{w['rain']} − slope ×{w['slope']})",
        fontsize=15, fontweight="bold")
    ax.set_xlabel("Longitude"); ax.set_ylabel("Latitude")
    cb = fig.colorbar(im, ax=ax, fraction=0.046, pad=0.04)
    cb.set_label("Suitability score (0 = poor, 1 = ideal)")
    plt.tight_layout()
    plt.savefig(os.path.join(OUT_DIR, "5_composite_suitability_map.png"), bbox_inches="tight")
    plt.show()
    plt.close()
    print("Saved 5_composite_suitability_map.png")


def plot_dataset_summary(soil_names, n_rain_months):
    fig, axes = plt.subplots(1, 3, figsize=(15, 5), dpi=200)
    fig.suptitle("ARIA Raw Dataset — Currently Loaded", fontsize=20,
                 fontweight="bold", y=1.02)

    cards = [
        {
            "label": "Soil Layers",
            "stat": str(len(soil_names)),
            "detail": ", ".join(soil_names),
        },
        {
            "label": "Rainfall Months Loaded",
            "stat": f"{n_rain_months}/{len(RAINFALL_FILES)}",
            "detail": "months of real CHIRPS rainfall data",
        },
        {
            "label": "Zone Suitability Weights",
            "stat": f"{ZONE_SUITABILITY_WEIGHTS['soil']}:{ZONE_SUITABILITY_WEIGHTS['rain']}:{ZONE_SUITABILITY_WEIGHTS['slope']}",
            "detail": "soil : rain : slope",
        },
    ]

    accent = "#1B5E20"
    for ax, card in zip(axes, cards):
        ax.axis("off")
        ax.add_patch(plt.Rectangle((0, 0), 1, 1, transform=ax.transAxes,
                                    facecolor="#E8F5E9", edgecolor=accent,
                                    linewidth=2, zorder=0))
        ax.add_patch(plt.Rectangle((0, 0.86), 1, 0.14, transform=ax.transAxes,
                                    facecolor=accent, edgecolor=accent,
                                    linewidth=2, zorder=1))
        ax.text(0.5, 0.93, card["label"], transform=ax.transAxes,
                ha="center", va="center", fontsize=13, fontweight="bold",
                color="white", zorder=2)
        ax.text(0.5, 0.55, card["stat"], transform=ax.transAxes,
                ha="center", va="center", fontsize=30, fontweight="bold",
                color=accent, zorder=2)
        ax.text(0.5, 0.18, card["detail"], transform=ax.transAxes,
                ha="center", va="center", fontsize=10.5, color="#333333",
                wrap=True, zorder=2)
        ax.set_xlim(0, 1)
        ax.set_ylim(0, 1)

    plt.tight_layout()
    plt.savefig(os.path.join(OUT_DIR, "6_dataset_summary.png"), bbox_inches="tight")
    plt.show()
    plt.close()
    print("Saved 6_dataset_summary.png")


def main():
    print("=" * 60)
    print("ARIA EDA Plot Generation")
    print("=" * 60)
    elevation, slope, soil, rainfall, soil_names, n_rain = load_layers()
    plot_layer_heatmaps(elevation, slope, soil, rainfall)
    plot_correlation_and_pairwise(elevation, slope, soil, rainfall)
    plot_composite_suitability(soil, rainfall, slope)
    plot_dataset_summary(soil_names, n_rain)
    print("=" * 60)
    print(f"All EDA plots saved to {OUT_DIR}")


if __name__ == "__main__":
    main()
