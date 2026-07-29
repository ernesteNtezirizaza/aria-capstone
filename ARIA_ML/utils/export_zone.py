"""
utils/export_zone.py
=====================
Exports a fixed set of real Rwanda zones (produced by zone_builder.py) to the
per-zone JSON format ARIA_Unity's RealZoneLoader.cs expects, plus the
zone_manifest.json that lists them for the in-sim zone-switch button.

This targets exactly the 9 zones already referenced by the deployed
zone_manifest.json (6 eval zones + 3 sampled train zones), so re-running it
after a fresh preprocess/zone_builder pass regenerates the same zone set with
current data, without changing which zones the demo cycles through.

Output goes to ARIA_Web/public/simulation/StreamingAssets/, matching where
the Unity WebGL build already looks (Application.streamingAssetsPath).

Run: python utils/export_zone.py
"""

import json
import os
import sys

import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from configs.config import ZONES_DIR

REPO_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

""" The Unity Editor/WebGL build reads its StreamingAssets straight from the
   project source, not from the already-built output under ARIA_Web/public --
   writing there would only update a stale copy the next build immediately
   overwrites. Assets/StreamingAssets/ is the one that actually matters. """
STREAMING_ASSETS_DIR = os.path.join(REPO_ROOT, "ARIA_Unity", "Assets", "StreamingAssets")

""" (split, array_index, output fileName) -- matches the existing deployed
   zone_manifest.json exactly, so the demo's zone-switch button keeps cycling
   through the same named zones after re-export. """
TARGET_ZONES = [
    ("eval",  0, "aria_zone_eval_0.json"),
    ("eval",  1, "aria_zone_eval_1.json"),
    ("eval",  2, "aria_zone_eval_2.json"),
    ("eval",  3, "aria_zone_eval_3.json"),
    ("eval",  4, "aria_zone_eval_4.json"),
    ("eval",  5, "aria_zone_eval_5.json"),
    ("train", 0, "aria_zone_train_0.json"),
    ("train", 17, "aria_zone_train_17.json"),
    ("train", 29, "aria_zone_train_29.json"),
]


def flatten_terrain(terrain_zone):
    """(size, size, ch) -> flat list in (y*size+x)*ch+c order, matching
    RealZoneLoader.cs's read loop exactly."""
    return terrain_zone.astype(np.float32).flatten().tolist()


def flatten_grid(grid_zone):
    """(size, size) -> flat list in y*size+x order."""
    return grid_zone.astype(np.float32).flatten().tolist()


def run():
    os.makedirs(STREAMING_ASSETS_DIR, exist_ok=True)
    print("ARIA -- Zone Export to Unity StreamingAssets")
    print("=" * 50)

    with open(os.path.join(ZONES_DIR, "zone_registry.json")) as f:
        registry = json.load(f)
    registry_by_key = {(z["split"], z["array_index"]): z for z in registry}

    arrays = {}
    for split in ("train", "eval"):
        arrays[split] = {
            "terrain":     np.load(os.path.join(ZONES_DIR, f"{split}_terrain.npy")),
            "disturbance": np.load(os.path.join(ZONES_DIR, f"{split}_disturbance.npy")),
            "obstacle":    np.load(os.path.join(ZONES_DIR, f"{split}_obstacle.npy")),
            "noplant":     np.load(os.path.join(ZONES_DIR, f"{split}_noplant.npy")),
        }

    manifest_entries = []
    for index, (split, array_index, file_name) in enumerate(TARGET_ZONES):
        meta = registry_by_key.get((split, array_index))
        if meta is None:
            raise KeyError(f"No zone in registry for split={split}, array_index={array_index}")

        terrain_zone = arrays[split]["terrain"][array_index]
        dist_zone    = arrays[split]["disturbance"][array_index]
        obs_zone     = arrays[split]["obstacle"][array_index]
        noplant_zone = arrays[split]["noplant"][array_index]
        size = terrain_zone.shape[0]
        n_channels = terrain_zone.shape[2]

        mean_soil = float(terrain_zone[:, :, 2].mean())

        payload = {
            "size": size,
            "nChannels": n_channels,
            "name": meta["name"],
            "agroZone": meta["agro_zone"],
            "split": split,
            "boundsLeft": meta["bounds"]["left"],
            "boundsRight": meta["bounds"]["right"],
            "boundsTop": meta["bounds"]["top"],
            "boundsBottom": meta["bounds"]["bottom"],
            "meanSoil": mean_soil,
            "noPlantPct": float(noplant_zone.mean() * 100),
            "terrainFlat": flatten_terrain(terrain_zone),
            "distGridFlat": flatten_grid(dist_zone),
            "obsGridFlat": flatten_grid(obs_zone),
            "noPlantFlat": [bool(v) for v in noplant_zone.flatten().tolist()],
        }

        out_path = os.path.join(STREAMING_ASSETS_DIR, file_name)
        with open(out_path, "w") as f:
            json.dump(payload, f)

        manifest_entries.append({
            "index": index,
            "fileName": file_name,
            "name": meta["name"],
            "agroZone": meta["agro_zone"],
            "split": split,
            "meanSoil": mean_soil,
        })

        elev = terrain_zone[:, :, 0]
        slope = terrain_zone[:, :, 1]
        print(f"  [{index}] {file_name:<24s} {meta['name']:<28s} "
              f"elev={elev.min():.2f}-{elev.max():.2f} slope_mean={slope.mean():.3f}")

    with open(os.path.join(STREAMING_ASSETS_DIR, "zone_manifest.json"), "w") as f:
        json.dump({"zones": manifest_entries}, f, indent=2)

    print(f"\nExported {len(manifest_entries)} zones to {STREAMING_ASSETS_DIR}")


if __name__ == "__main__":
    run()
