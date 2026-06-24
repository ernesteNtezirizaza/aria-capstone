"""
utils/zone_builder.py
=====================
Slices the full Rwanda terrain into 18 zone sub-grids.
Each zone: 120×120 cells = 30km×30km at 250m resolution.
Split: 12 training zones, 6 held-out evaluation zones.

Run: python utils/zone_builder.py
"""

import os, sys, json
import numpy as np

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from configs.config import (
    ZONE_DEFINITIONS, ZONE_SIZE, GRID_COLS, GRID_ROWS,
    RWANDA_BOUNDS, DATA_PROC_DIR, ZONES_DIR
)


def get_bounds(col, row):
    lx = (RWANDA_BOUNDS["right"] - RWANDA_BOUNDS["left"]) / GRID_COLS
    ly = (RWANDA_BOUNDS["top"]   - RWANDA_BOUNDS["bottom"]) / GRID_ROWS
    return {
        "left":   round(RWANDA_BOUNDS["left"] + col * ZONE_SIZE * lx, 4),
        "right":  round(RWANDA_BOUNDS["left"] + (col+1) * ZONE_SIZE * lx, 4),
        "top":    round(RWANDA_BOUNDS["top"]  - row * ZONE_SIZE * ly, 4),
        "bottom": round(RWANDA_BOUNDS["top"]  - (row+1) * ZONE_SIZE * ly, 4),
    }


def extract(grid, col, row):
    rs = row * ZONE_SIZE
    re = min(rs + ZONE_SIZE, grid.shape[0])
    cs = col * ZONE_SIZE
    ce = min(cs + ZONE_SIZE, grid.shape[1])

    if grid.ndim == 3:
        patch = np.zeros((ZONE_SIZE, ZONE_SIZE, grid.shape[2]), dtype=grid.dtype)
        patch[:re-rs, :ce-cs] = grid[rs:re, cs:ce]
    else:
        patch = np.zeros((ZONE_SIZE, ZONE_SIZE), dtype=grid.dtype)
        patch[:re-rs, :ce-cs] = grid[rs:re, cs:ce]
    return patch


def run():
    os.makedirs(ZONES_DIR, exist_ok=True)
    print("ARIA — Zone Builder")
    print("=" * 50)

    terrain  = np.nan_to_num(np.load(os.path.join(DATA_PROC_DIR, "terrain_grid.npy")),    nan=0.0)
    dist     = np.nan_to_num(np.load(os.path.join(DATA_PROC_DIR, "disturbance_map.npy")), nan=0.0)
    obs      = np.nan_to_num(np.load(os.path.join(DATA_PROC_DIR, "obstacle_map.npy")),    nan=0.0)
    rain     = np.nan_to_num(np.load(os.path.join(DATA_PROC_DIR, "rainfall_stack.npy")),  nan=0.0)
    no_plant = np.load(os.path.join(DATA_PROC_DIR, "no_plant_mask.npy"))

    registry = []
    tr_t, tr_d, tr_o, tr_r, tr_n = [], [], [], [], []
    ev_t, ev_d, ev_o, ev_r, ev_n = [], [], [], [], []
    ti = ei = 0

    for zid, col, row, name, agro, split in ZONE_DEFINITIONS:
        tp = extract(terrain,  col, row)
        dp = extract(dist,     col, row)
        op = extract(obs,      col, row)
        rp = rain[:, row*ZONE_SIZE:row*ZONE_SIZE+ZONE_SIZE,
                     col*ZONE_SIZE:col*ZONE_SIZE+ZONE_SIZE]
        np_p = extract(no_plant, col, row)

        # Pad rainfall if needed
        if rp.shape[1] < ZONE_SIZE or rp.shape[2] < ZONE_SIZE:
            tmp = np.zeros((rain.shape[0], ZONE_SIZE, ZONE_SIZE), dtype=rain.dtype)
            tmp[:, :rp.shape[1], :rp.shape[2]] = rp
            rp = tmp

        idx = ti if split == "train" else ei
        registry.append({
            "zone_id": zid, "name": name, "agro_zone": agro,
            "split": split, "col": col, "row": row,
            "bounds": get_bounds(col, row), "array_index": idx,
            "mean_soil": float(tp[:,:,2].mean()),
            "no_plant_pct": float(np_p.mean() * 100),
            "mean_dist": float(dp.mean()),
        })

        if split == "train":
            tr_t.append(tp); tr_d.append(dp); tr_o.append(op)
            tr_r.append(rp); tr_n.append(np_p)
            ti += 1
        else:
            ev_t.append(tp); ev_d.append(dp); ev_o.append(op)
            ev_r.append(rp); ev_n.append(np_p)
            ei += 1

        print(f"  Z{zid:02d} [{split:5s}] {name:<32} "
              f"soil={tp[:,:,2].mean():.2f} dist={dp.mean():.2f}")

    # Save
    for prefix, tl, dl, ol, rl, nl in [
        ("train", tr_t, tr_d, tr_o, tr_r, tr_n),
        ("eval",  ev_t, ev_d, ev_o, ev_r, ev_n),
    ]:
        np.save(os.path.join(ZONES_DIR, f"{prefix}_terrain.npy"),
                np.stack(tl, 0).astype(np.float32))
        np.save(os.path.join(ZONES_DIR, f"{prefix}_disturbance.npy"),
                np.stack(dl, 0).astype(np.float32))
        np.save(os.path.join(ZONES_DIR, f"{prefix}_obstacle.npy"),
                np.stack(ol, 0).astype(np.float32))
        np.save(os.path.join(ZONES_DIR, f"{prefix}_rainfall.npy"),
                np.stack(rl, 0).astype(np.float32))
        np.save(os.path.join(ZONES_DIR, f"{prefix}_noplant.npy"),
                np.stack(nl, 0))

    with open(os.path.join(ZONES_DIR, "zone_registry.json"), "w") as f:
        json.dump(registry, f, indent=2)

    print(f"\nZone building complete.")
    print(f"  Training zones: {ti}")
    print(f"  Eval zones:     {ei}")


if __name__ == "__main__":
    run()
