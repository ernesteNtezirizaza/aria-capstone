# ARIA Unity Demo

Autonomous drone reforestation simulation, driven by your actual
trained `ppo_exp_02` PPO policy (exported to ONNX and numerically
verified against the source PyTorch model: max difference 0.00000048,
exact argmax match).

---

## What this project actually is

This Unity project faithfully ports the **simulation logic** (weather,
battery, seed growth/lifecycle, disturbance, reseeding monitoring,
action dispatch) from your real `env/*.py` source files, line-by-line
verified against the code, and runs your **real trained model weights**
via ONNX inference (Unity Sentis) to make every movement/seeding/
mission decision.

It does **not** use real Rwanda GIS data. See "Known limitations and
approximations" below before treating any specific in-demo decision
as a scientifically meaningful Rwanda recommendation.

---

## Setup

1. **Unity version**: 2023.2+ required for Sentis. (Note: this
   project went through a real detour during development -- started
   on Unity 2022.3, hit a Sentis "package cannot be found" error,
   tried Barracuda as a 2022.3-compatible fallback, found Barracuda
   unavailable in registry search, and ultimately upgraded to Unity 6
   LTS. If you're on 2022.3 and hit the same wall, see
   `ARIAPolicyInference_Barracuda.cs.bak` for the Barracuda
   alternative, though its availability is unconfirmed.)
2. **Open this folder** as a Unity project.
3. **Install Sentis via Package Manager search** (Unity Registry tab,
   search "Sentis", click Install). Do **not** assume a specific
   version -- a previous attempt hardcoded `1.4.0` into
   `manifest.json` and Package Manager could not resolve it. Install
   whatever version search actually offers, then optionally add that
   exact version to `manifest.json` for reproducibility.
4. Confirm `Assets/StreamingAssets/aria_policy.onnx` imports as a
   Sentis `ModelAsset` in the Project window (not a generic file
   icon) -- this only works once Sentis is genuinely installed.
5. **Easiest path -- use SceneBootstrapper:**
   - Create a new empty Scene.
   - Create one empty GameObject, attach
     `ARIA.Drone.SceneBootstrapper` to it.
   - Drag the imported `aria_policy.onnx` ModelAsset into the
     bootstrapper's `onnxModel` field in the Inspector.
   - Press Play. SceneBootstrapper builds the Drone (capsule
     primitive + ARIAPolicyInference + DroneController), a coloured
     ground plane (TerrainRenderer), a camera, and a light, all in
     code -- no further manual GameObject wiring needed.
6. **Manual alternative** (if you want individual control over each
   GameObject instead of the bootstrapper): create the Drone
   GameObject yourself and attach `ARIAPolicyInference` +
   `DroneController` directly, wiring the `onnxModelAsset` and
   `policyInference` fields by hand in the Inspector. Add
   `TerrainRenderer` to a separate GameObject and assign its `drone`
   field. This is more steps but useful if you want a custom drone
   mesh or camera setup instead of the bootstrapper's defaults.
7. **Optional HUD**: create a Canvas with `UnityEngine.UI.Text`
   elements, add `ARIA.UI.DroneHUD` to any GameObject, assign the
   `Drone` reference (find it in Hierarchy after pressing Play once,
   if using SceneBootstrapper) and the Text fields you want populated.
8. The drone should rise (cosmetic takeoff), fly briefly to its
   starting cell (cosmetic navigating), then begin real model-driven
   decisions every `stepInterval` seconds, visibly seeding cells on
   the coloured terrain.

---

## Project structure

```
ARIA_Unity/
├── Assets/
│   ├── Scripts/
│   │   ├── Core/           -- constants, zone data, episode state, action dispatch
│   │   │   ├── ARIAConstants.cs      Every numeric threshold, mirrored from config.py
│   │   │   ├── ZoneData.cs           Procedural terrain generator (NOT real GIS data)
│   │   │   ├── EpisodeState.cs       Per-episode state + observation builder
│   │   │   └── ActionDispatcher.cs   Full action-execution logic (step() port)
│   │   ├── Systems/        -- faithful ports of env/*.py simulation modules
│   │   │   ├── WeatherSystem.cs
│   │   │   ├── EnergySystem.cs
│   │   │   ├── GrowthEngine.cs
│   │   │   ├── DisturbanceEngine.cs
│   │   │   └── MonitoringSystem.cs
│   │   ├── ML/              -- ONNX inference (UNVERIFIED, see below)
│   │   │   ├── ARIAPolicyInference.cs
│   │   │   └── ActionSelector.cs
│   │   ├── Drone/
│   │   │   ├── DroneController.cs    Main orchestrator + cosmetic intro
│   │   │   ├── TerrainRenderer.cs    Visualises ZoneData as a coloured ground plane
│   │   │   └── SceneBootstrapper.cs  Builds the whole scene in code -- easiest setup path
│   │   ├── UI/
│   │   │   └── DroneHUD.cs
│   │   └── ARIA.Runtime.asmdef       References Unity.Sentis (name unverified)
│   └── StreamingAssets/
│       └── aria_policy.onnx          Your REAL, verified, exported model
├── Packages/
│   └── manifest.json                 Sentis version unverified
└── .gitignore
```

---

## Known limitations and approximations (full list)

Read this section before presenting the demo or discussing it with
your supervisor -- every item here is something a careful reviewer
could reasonably ask about, and you should be able to answer
precisely, not be caught off guard.

### 1. Synthetic terrain, not real Rwanda data
`ZoneData.cs` / `ZoneGenerator` produces procedurally generated
terrain (Perlin noise, calibrated to roughly the same value ranges as
real data) -- not the actual DEM/soil/CHIRPS/landcover rasters your
model was trained on. **The model's weights and decision logic are
real and verified.** The terrain it's reacting to in this demo is not.
If you want full scientific fidelity, replace `ZoneGenerator` with a
loader that reads exported `.npy`/`.json` zone data from your actual
Python pipeline (`all_terrain` / `all_dist` / `all_obs` / `all_noplant`
in `rwanda_env.py`).

### 2. Threshold/species values are config.py's documented fallbacks
`ARIAConstants.cs`'s `RAINFALL_SUNNY_THRESH` (0.266), `ZONE_MIN_SOIL`
(0.358), and the 5-species list with their `rain_min` values are
copied from `config.py`'s explicit fallback block (used when live
dataset derivation fails), **not necessarily the exact values printed
during your specific `ppo_exp_02` training run** (those are derived
at runtime from your dataset and could differ slightly). If you have
your training log, search for the printed threshold values and update
the constants file for full fidelity.

### 3. Two states are cosmetic-only (Takeoff, Navigating)
Verified directly against `rwanda_env.py`: `reset()` sets
`drone_state` straight to `STATE_SEEDING`, with no takeoff or
navigating phase at all during training. The model has **no learned
behaviour** tied to `STATE_TAKEOFF` / `STATE_NAVIGATING`. Per your
explicit request, `DroneController.cs` plays a brief **scripted,
fixed-duration animation** for these two states at the start of each
episode -- clearly labelled `[intro, not model-driven]` in the HUD,
and the ONNX model is not called during this sequence.

### 4. Rainfall has no seasonal variation in the synthetic zone
The real env tracks a separate per-season rainfall layer
(`rain_stack`, 6 layers) that updates the terrain's rainfall channel
each step as seasons advance. The synthetic `ZoneData` has only one
static rainfall channel. `EpisodeState.cs` and `ActionDispatcher.cs`
both flag this simplification explicitly in code comments at the
exact lines affected.

### 5. ONNX inference (Sentis) is unverified
**Everything else in this project was checked**: formulas
cross-referenced line-by-line against the real Python source, braces
balanced, every method/field call cross-referenced against its actual
declared public API. The ONNX export itself (`export_to_onnx.py`) was
**actually run** against your real checkpoint and numerically verified.
But `ARIAPolicyInference.cs` (the Sentis wrapper) has **not** been
compiled or run -- there is no Unity/Sentis install available in the
environment that generated this project. The API calls
(`ModelLoader.Load`, `WorkerFactory.CreateWorker`, `TensorFloat`,
`worker.SetInput`/`Schedule`/`PeekOutput`) are written against
documented Sentis 1.x conventions but may need adjustment once you
actually compile this in Unity and the compiler reports real errors.
This is expected, normal, and not a sign the ONNX export itself is
wrong (that part is independently verified).

### 6. A real bug was found and fixed during this build
`EpisodeState.ResetEpisode()` initially defaulted the drone's spawn
position to zone-centre on every episode. Cross-checking against
`rwanda_env.py`'s `reset()` (which calls `_valid_start()` -- a
randomised position avoiding no-plant/protected cells) caught this
as a real discrepancy, now fixed (`ValidStart()` in `EpisodeState.cs`).
Mentioned here for transparency about the verification process, not
because it remains an open issue.

### 7. TerrainRenderer's orientation is unverified
`TerrainRenderer.cs` paints `ZoneData` directly onto a `Texture2D`
with no Y-axis flip. Unity's texture pixel origin is bottom-left;
whether this produces a visually "north-up" terrain matching the
drone's actual flight direction has not been visually confirmed (no
Unity install available to check). If the terrain looks vertically
mirrored relative to where the drone flies once you press Play, flip
the Y index in `RebuildTexture()`'s pixel-array loop.

### 8. SceneBootstrapper uses reflection for the model field
`SceneBootstrapper.cs` assigns your dragged ONNX asset to
`ARIAPolicyInference.onnxModelAsset` via reflection (`GetField`/
`SetValue`) rather than a direct typed reference. This is deliberate
-- it lets the bootstrapper work unchanged whether `ARIAPolicyInference`
is currently the Sentis or Barracuda version, without needing edits
every time you switch backends (which this project has now done
twice). The tradeoff: a typo in the field name would fail silently at
runtime with a logged error, not a compile-time error. If inference
never seems to run and the Console shows no errors, check for that
warning specifically.

---

## If something doesn't compile

Given the Sentis-API uncertainty above, the most likely compile
errors will be in `ARIAPolicyInference.cs`. Common things to check
first:
- Confirm the actual namespace/class names Sentis exposes in your
  installed version (`Unity.Sentis` vs `Unity.Barracuda` if you're on
  an older Unity/package combination -- Barracuda was Sentis's
  predecessor and has a different API).
- Confirm `TensorFloat` accepts a `(TensorShape, float[])` constructor
  in your installed version -- some Sentis versions may require a
  different tensor-construction pattern.
- Confirm `IWorker`'s execution API (`Schedule()` + `PeekOutput()` vs.
  an `Execute()` + `CopyOutput()` pattern, depending on version).

Every other script in this project (Core, Systems, Drone, UI) uses
only base C#/UnityEngine APIs that have been stable for years and are
much lower-risk.
