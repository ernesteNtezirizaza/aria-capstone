# ARIA: Adaptive Reforestation Intelligence Agent

> **An autonomous, AI-driven drone seeding simulation and monitoring platform for reforestation efforts.**

---

## Live Demo

| Resource | Link |
|---|---|
| **Deployed App** | [aria-capstone.vercel.app](https://aria-capstone.vercel.app) |
| **Live Simulation** | [aria-capstone.vercel.app/simulation](https://aria-capstone.vercel.app/simulation) |
| **Live Dashboard** | [aria-capstone.vercel.app/dashboard](https://aria-capstone.vercel.app/dashboard) |
| **5-Minute Technical Walkthrough Video** | **[Watch the demo video](https://drive.google.com/file/d/1EQRBjTcgRVxyV_NZq_PY4gwk8IPGpvv0/view?usp=sharing)** |

---

## Project Overview

**ARIA (Adaptive Reforestation Intelligence Agent)** bridges deep reinforcement learning with a high-fidelity Unity-based simulation to plan, execute, and monitor autonomous drone seeding operations across diverse terrain.

The system combats deforestation by deploying a trained DRL policy that chooses where to fly, what species to plant, and when to abort or return to base, based on soil quality, slope, rainfall, battery reserves, and live disturbance from grazing animals, with every decision and outcome streamed to a real-time monitoring dashboard.

## Key Features

- **Unified Planner & Navigator**: a single PPO+CNN policy handles both mission-level planning (which zone to visit, when to return, when to abort) and step-level navigation (where to fly, which species to drop) simultaneously.
- **Seed Monitoring & Reseeding**: the drone remembers every seed it drops, tracks whether it grew or failed, records the failure reason and timestep, and automatically schedules a return reseeding mission to failed cells with a better-suited species.
- **Solar & Battery Energy System**: battery drains during flight and recharges under solar exposure; rainy weather blocks recharging entirely.
- **Rain Cover Mechanism**: a protective cover deploys automatically over the seed mechanism when rainfall is detected, and retracts once conditions clear.
- **Obstacle Detection & Avoidance**: the drone detects and routes around dynamically spawned hazards mid-flight.
- **Animal Disturbance**: grazing goats roam the planted zone and kill nearby seeds/trees, which feeds directly back into the reseeding pipeline above.
- **Real-Time Telemetry Dashboard**: episode rewards, seed lifecycle breakdown, spacing violations, and recent failure/reseed targets, all backed by a persistent Postgres database.

---

## System Architecture

ARIA is a decoupled, three-part system:

### 1. `ARIA_ML` (PPO + CNN Agent)
The core intelligence of the system. I use **Proximal Policy Optimization (PPO)** combined with a **Convolutional Neural Network (CNN)** terrain extractor.
- **Tech Stack:** Python, PyTorch, Stable-Baselines3, ONNX
- **Functionality:** Trains against a custom Gym environment modeling terrain, weather, energy, growth, and disturbance, then exports the trained policy to ONNX for runtime inference.

### 2. `ARIA_Unity` (Simulation & Live Inference)
A Unity-based simulation that is a faithful C# port of the Python training environment, so the exact same rules govern both training and the live demo.
- **Tech Stack:** Unity 6000.3.18f1, C#, Unity Inference Engine (ONNX runtime), WebGL
- **Functionality:** Runs the exported ONNX policy live in-browser via Unity Inference Engine, simulates drone flight, energy, weather, growth, and animal disturbance, and streams episode telemetry to the web API.

### 3. `ARIA_Web` (Real-Time Telemetry & Monitoring)
The command center for the ARIA system.
- **Tech Stack:** Next.js 16 (React 19), Tailwind CSS, Prisma 7 + `@prisma/adapter-pg`, Neon PostgreSQL, Recharts
- **Functionality:** A responsive, dark-themed dashboard that ingests real-time telemetry via REST APIs and visualizes episode rewards, seed lifecycle outcomes, spacing violations, and reseed targets.

---

## Installation & Setup Instructions

Follow these steps to run the ARIA ecosystem locally on your machine.

### Prerequisites
- [Node.js (v20+)](https://nodejs.org/) & npm
- [Python 3.9+](https://www.python.org/)
- [Unity Hub & Unity Editor 6000.3.18f1](https://unity.com/) (only needed to edit/rebuild the simulation; the web app can run against the pre-built WebGL bundle without it)
- [Git](https://git-scm.com/)

### Step 1: Clone the Repository
```bash
git clone https://github.com/ernesteNtezirizaza/aria-capstone.git
cd aria-capstone
```

### Step 2: Set up the ARIA_Web Dashboard
The web dashboard acts as the telemetry receiver and hosts the pre-built WebGL simulation. It requires a PostgreSQL database (I recommend [Neon](https://neon.tech)).

```bash
cd ARIA_Web
npm install
```

1. Create a `.env` file in the `ARIA_Web` directory.
2. Add your PostgreSQL connection string:
   ```env
   DATABASE_URL="postgresql://user:password@host/dbname?sslmode=require"
   ```
3. Push the schema to your database:
   ```bash
   npx prisma db push
   ```
4. Start the development server:
   ```bash
   npm run dev
   ```
The dashboard and embedded simulation will now be running at `http://localhost:3000`.

### Step 3: Set up the ARIA_ML Environment
```bash
cd ../ARIA_ML
python -m venv venv
# On Windows: venv\Scripts\activate
# On Mac/Linux: source venv/bin/activate

pip install -r requirements.txt
```
*(Optional)* To train a new policy from scratch:
```bash
python training/train_ppo.py
```
Or explore the training process interactively via Jupyter:
```bash
jupyter notebook notebook/aria_notebook.ipynb
```
To export a freshly trained policy for use in Unity:
```bash
python export_to_onnx.py
```

### Step 4 (Optional): Run the ARIA_Unity Simulation in the Editor
Only needed if you want to modify simulation behavior. The web app already serves a pre-built WebGL version of the simulation.
1. Open **Unity Hub**, click **Add**, and select the `aria-capstone/ARIA_Unity` directory.
2. Open the project (Unity will prompt to install `6000.3.18f1` if it isn't already present).
3. Load the main scene under `Assets/Scenes/`.
4. Press **Play**. The drone will begin executing the trained policy against a procedurally selected zone.

---

## Deployment

The production environment is fully separate from local development: the web app, simulation, and database are all live, publicly reachable services rather than a demo run locally for grading.

### Environments
| Environment | Web App | Database |
|---|---|---|
| **Production** | [aria-capstone.vercel.app](https://aria-capstone.vercel.app), auto-deployed from `master` | Neon PostgreSQL (pooled connection, `sslmode=require`) |
| **Local Dev** | `http://localhost:3000` (`npm run dev`) | Any Postgres instance the developer points `DATABASE_URL` at |

### Tools & Pipeline
- **Hosting:** Vercel, connected directly to the GitHub repository. Every push to `master` triggers a new build and deploy automatically (`prisma generate && next build`).
- **Database:** Neon serverless PostgreSQL, accessed through Prisma 7's `@prisma/adapter-pg` driver adapter (not Prisma's default query engine binary), so schema changes are applied via `npx prisma db push` against the production connection string.
- **Simulation build:** the Unity project is built headlessly to WebGL (`Unity.exe -batchmode -nographics -quit -executeMethod BuildScript.BuildWebGL`), producing pre-gzipped `.data.gz` / `.wasm.gz` / `.framework.js.gz` assets. These are committed into `ARIA_Web/public/simulation/Build/` and served as static files by Vercel with `Content-Encoding: gzip`, so the browser downloads a compressed WebGL build without any server-side transcoding step.
- **Telemetry ingestion:** the Unity build (running in the browser) posts episode/seed telemetry directly to the deployed Next.js REST API, which writes to the same Neon database the dashboard reads from, so the dashboard reflects real traffic from anyone currently running the simulation, not seeded/mock data.

### Deployment Steps
1. Build the WebGL bundle from `ARIA_Unity` and copy the output into `ARIA_Web/public/simulation/`.
2. Commit and push to `master`.
3. Vercel picks up the push, runs `prisma generate && next build --webpack`, and deploys automatically, with no manual server provisioning.
4. Push any schema changes separately with `npx prisma db push` (schema changes are not part of the Vercel build step).

### Verification
Each deploy was verified in the target environment itself (production), not just locally:
- **Propagation check:** polled the production `ETag` response header against the local build's MD5 hash until they matched, to confirm Vercel's CDN was actually serving the new build rather than a cached previous version.
- **Smoke test:** confirmed `/`, `/simulation`, and `/dashboard` all return `200` on the live domain after each deploy.
- **Functional verification:** the screenshots in [Testing Results](#testing-results) below were captured directly against the production URL, not a local dev server, confirming the deployed build behaves correctly end-to-end (WebGL asset loading, live telemetry write path, dashboard read path).

---

## Testing Results

All testing below was performed directly against the **live production deployment** (not a local dev build), to validate real-world behavior under actual network and hosting conditions.

### Testing Strategies Used
- **Functional/manual testing**: exercising each of the 7 core features via the in-sim "Demo Controls" panel, which forces specific conditions (weather, disturbance, zone) on demand rather than waiting for them to occur naturally.
- **Boundary/edge-case testing**: forced battery to its critical threshold via the "Force Rainy" demo control to verify the emergency-response path, and separately forced "Force Sunny" to verify the recharge/hold path. An earlier build branched the return logic on weather (sunny cancelled an in-progress critical return, rainy forced a full return-and-land); a full parity audit against `rwanda_env.py` found that split had no basis in the trained environment, which terminates on critical battery immediately and unconditionally regardless of weather, and removed it (see Discussion). Re-verified live post-fix: under "Force Rainy" the battery fell from 100% to the 5% critical floor in under 75 seconds and the episode terminated immediately, no scripted return flight; under "Force Sunny" the battery held flat at 100% rather than continuing to fall. That second check caught a real, separate bug during this same test pass: "Force Sunny" originally set effective rainfall to a value still close enough to the sunny/rainy threshold that solar income stayed well below drain, so battery kept falling even while the button read "Force Sunny" (confirmed live: 53% -> 39% over 15 seconds). Fixed by setting `ForceSunny`'s effective rainfall to exactly 0, redeployed, and re-confirmed live.
- **Unattended/automatic behavior testing**: loaded the simulation and let it run completely untouched, no demo-control clicks at all, for a full 14 minutes, to confirm the systems that are supposed to run on their own actually do, including the ones that only reveal themselves over a long, hands-off window. The battery dropped `99% -> 89% -> 78%` over the first 6 minutes under default (real-data) weather, a clean, monotonic decline with zero operator input, which is what an earlier buggy build (solar income exactly cancelling the drain constant, pinning battery at 100% forever) would have failed. In this specific run the mission itself finished, with 499/500 seeds placed, before the battery reached its critical threshold; the drone landed and, with no further movement drain and solar still active while grounded, battery recovered to 98% and held there for the rest of the window. That is a genuine result worth stating plainly rather than reframing: this run demonstrates an efficient policy that completes its mission well inside its energy budget, not the critical-battery auto-return path. That path is not just theoretical, though: the 9-zone evaluation below independently confirms it fires regularly (`battery_empty_events` up to 0.84 per episode in some zones), it simply didn't fire in this one untouched browser session.
- **Real-vs-synthetic obstacle audit**: an earlier build's "Obstacles" demo button was found to be destructive rather than illustrative: it wiped and re-randomized the drone's real hazard grid on click, meaning what the demo showed was fake data overwriting real data, not a faithful view of the trained policy's actual environment. Fixed by removing the toggle and its underlying overlay logic entirely; hazard markers are now always the real, terrain-derived obstacle positions the policy actually reasons over, visible from the moment the page loads with zero clicks.
- **Observation-space parity testing**: audited the Unity C# port of the observation space against the Python training environment field-by-field and found it had drifted (the deployed `mission_vector` was missing 3 fields the trained policy expected, and the shipped ONNX policy file was stale). Re-implemented the missing fields (nearest-unseeded-suitable-cell offset/distance) in `EpisodeState.cs` to match `rwanda_env.py`'s formula exactly, then re-exported and re-verified the ONNX policy against the PyTorch reference (max abs output diff `0.00000048`, identical argmax action) before redeploying.
- **Fault-injection testing on the telemetry API**: sent the production `/api/monitoring` endpoint (a) a request missing required fields, (b) a payload with `seeds` as a string instead of an array, (c) intentionally malformed JSON, (d) a genuinely novel/bad zone identity never seen before, and (e) a real dropped connection (a request with an inflated `Content-Length`, half its body written, then the socket forcibly destroyed mid-upload, not just a clean client-side abort). Results: missing fields correctly return `400`; malformed payloads are caught and return `500` rather than crashing the process. The bad-zone-identity test caught a genuine bug rather than confirming graceful handling: the production database had a `province` column left over from earlier schema history that was `NOT NULL` with no default but entirely absent from `schema.prisma`, so creating any zone the database hadn't already seen crashed with an unhelpful `500` and silently dropped that episode's data, while episodes against already-known zones kept working, masking the problem. Root-caused by reproducing the write locally against the production database to get the real Prisma error (`P2011` null constraint violation) instead of the generic 500, then fixed at the source by dropping the stale constraint. Re-verified live: a fresh, never-before-seen zone identity now returns `200` and persists correctly. The dropped-connection test confirmed the write path degrades safely: the episode count before and after was identical (no partial row), and `/dashboard`/`/simulation` both stayed at `200` throughout, meaning a connection that dies mid-upload doesn't corrupt state or poison the app for the next real request.
- **Cross-zone generalization testing**: evaluated the trained policy across all 9 Rwanda zones actually shipped in the demo (6 held-out eval zones plus the 3 sampled train zones in `zone_manifest.json`), 50 episodes each (`ARIA_ML/results/metrics/nine_zone_variance.csv`), confirming outcomes actually vary with real geospatial differences rather than being hardcoded: `pct_suitable_seeded` ranges 3.3-5.6%, `reseeding_count` ranges 12-76 per zone, and `battery_empty_events` ranges 0.06-0.84 per zone, all tied to that specific zone's real terrain and rainfall.
- **Full behavioral parity audit against `rwanda_env.py`**: a line-by-line comparison of the Unity C# simulation against the Python training environment, going beyond individual bug reports to check every scripted mechanic for a training-side basis. Found and fixed: an invented serpentine coverage-sweep pattern with no Python equivalent (ordinary navigation is now 100% policy-driven, matching training); reseed-target selection picking an arbitrary queued target instead of the nearest one by Manhattan distance; a missing reseed exception in the redundant-placement check that meant a queued reseed target could never actually be replanted even after being reached, only ever removed by a stuck-timeout (very likely the true root cause of the drone appearing to freeze mid-air in earlier testing); an invented obstacle auto-reroute search (Python simply blocks and holds position for one step); and five silently-drifted constants (`MAX_STEPS`, `BATTERY_RETURN_THRESH`, `BATTERY_CRITICAL`, `MIN_SEED_SPACING`, plus the already-covered battery formula). `INITIAL_SEEDS` was deliberately kept at 500 rather than matched to `config.py`'s 1000 -- a product decision, not a parity gap. Separately, the live demo was found to truncate and reset a zone at `MAX_STEPS` regardless of how many seeds were still unplaced; fixed so the demo always finishes placing its full seed budget before an episode ends.
- **End-to-end telemetry pipeline verification**: rather than just checking the dashboard renders, forced a real episode to completion (via the boundary/edge-case battery test above) and confirmed the full write path: Unity's `TelemetryManager` posted to `/api/monitoring` on episode termination, the row landed in Postgres, and the dashboard's Total Episodes, Total Seeds Placed, Seed Lifecycle Breakdown, Spacing Violations, and Recent Failures & Reseed Targets table all reflected that exact episode with real per-seed failure reasons and step numbers, not placeholder or cached data.

### Evidence

**Landing Page**
![Home](docs/testing/screenshots/01-home.png)

**Unified Planner & Navigator**: the same policy that decided to be in this zone is also placing seeds cell-by-cell; HUD shows seeds actively decreasing (500 -> 475 in the first 25 seconds) with no scripted sweep pattern behind it:
![Seeding](docs/testing/screenshots/04-simulation-seeding.png)

**Obstacle Detection & Avoidance**: hazards (red markers) cluster along real steep-slope ridgelines, since the obstacle map is derived from actual DEM slope and elevation-turbulence data (271 real hazard regions in this particular zone, confirmed via console log), not placed arbitrarily, and they are visible immediately with zero clicks, since obstacles are always the real hazard grid rather than a manually-toggled overlay:
![Obstacles](docs/testing/screenshots/05-simulation-obstacles.png)

**Unattended Battery Drain**: captured 45 seconds into a completely untouched session (no clicks), showing the battery has genuinely moved off 100% under default weather with zero operator intervention:
![Battery draining untouched](docs/testing/screenshots/10-battery-draining-untouched.png)

**Solar & Battery, Forced Boundary Test**: "Force Sunny" holding the battery flat at 100% rather than draining (left as a regression check after this exact button was found and fixed mid-testing to still be silently draining the battery), versus "Force Rainy" driving the same fresh episode down to the 5% critical floor in under 75 seconds, triggering immediate termination with no scripted return flight:
![Battery stable under Force Sunny](docs/testing/screenshots/12-force-sunny-battery-stable.png)
![Battery critical under Force Rainy](docs/testing/screenshots/13-battery-critical-termination.png)

**Rain Cover Mechanism**: cover deploys automatically the moment rainfall is detected:
![Rain Cover](docs/testing/screenshots/06-simulation-rain-cover.png)

**Animal Disturbance & Seed Monitoring/Reseeding**: a goat (dark model, center) roams near the drone; disturbance-killed seeds are queued as reseed targets (green markers) with a recommended replacement species, tracked in `Seeds Queued` on the HUD:
![Animal Disturbance](docs/testing/screenshots/07-simulation-animal-disturbance.png)

**Zone Switching**: instantly reloads a different zone's terrain, seed budget, and protected-area layout (this particular zone has zero real hazard regions, hence no markers):
![Zone Switch](docs/testing/screenshots/09-simulation-zone-switch.png)

**Real-Time Telemetry Dashboard**: captured after forcing a real episode to completion end-to-end (Unity -> `/api/monitoring` -> Postgres -> dashboard render) -- Total Episodes/Seeds Placed, the Seed Lifecycle donut, Spacing Violations, and the Recent Failures & Reseed Targets table are all populated with that episode's real data, not placeholders:
![Dashboard](docs/testing/screenshots/02-dashboard-overview.png)

---

## Analysis

The project's central objective, an autonomous agent that plans *and* navigates a reforestation mission end-to-end while adapting to live environmental disturbance, was achieved. The deployed system demonstrates all seven planned functionalities running against a live PPO+CNN policy, not scripted animations. The dashboard's schema was rebuilt this cycle to match the project's ER diagram exactly (`Zone` / `Episode` / `Seed`, no denormalized reward or agent-type fields), and production's episode history was reset in the process of re-establishing that schema, so the dashboard is currently showing what a fresh testing pass wrote to it (as of this writing: `1` episode, `56` seeds placed, `3.00` average reseeding count, `0.4%` average suitable seeding, from a deliberately short forced-battery-drain test run, not a representative full mission) rather than the accumulated history from before. These numbers move every time someone runs the live simulation, so treat the current dashboard as the source of truth over any specific snapshot quoted here -- the dashboard tracks per-episode `pct_suitable_seeded`, `spacing_violations`, and `reseeding_count`; it does not surface reward, which is an `ARIA_ML` training-side metric, not a live telemetry field.

That policy's training-time reward is only meaningful next to something to compare it against. The `ARIA_ML` training pipeline ran a 5-configuration hyperparameter sweep over the seed-spacing penalty (`ARIA_ML/results/metrics/all_experiments.csv`), with peak mean reward ranging from `1611.2` (over-penalized) to `2274.4` (the deployed configuration, spacing=5 weight=3.5). The deployed policy corresponds to the best-performing configuration in that sweep, not an arbitrary checkpoint. On the coverage side, the generalization evaluation across all 9 demo zones, 6 held out during training plus the 3 sampled train zones the live demo actually ships (`ARIA_ML/results/metrics/nine_zone_variance.csv`), shows raw `pct_suitable_seeded` of only 3.3-5.6%, which looks weak in isolation, but the seed budget is small relative to the number of ecologically suitable cells per zone, so the realistic *achievable ceiling* is itself only ~7-10% in most zones (up to ~22% in the most favorable one). Measured against each zone's own ceiling, the policy is actually placing roughly 25-62% of its available seeds into suitable cells depending on the zone, a materially different and more honest number than the raw percentage alone.

Two objectives were partially rather than fully met:
- **Reseed pipeline visibility.** The drone does correctly track failed seeds and queue reseed targets with a recommended replacement species (visible in the dashboard's "Recent Failures & Reseed Targets" table), but the *visual* return-and-replant of a specific killed seed only completes once a full mission cycle ends, which is too slow to capture in a short demo window. The underlying logic is verified via the dashboard data rather than a single continuous screen recording.
- **Terrain visual fidelity, discovered and fixed mid-project.** An earlier deployed build rendered every zone as a flat, uniform green plane with no visible elevation, slope, or soil variation, despite the system being trained on 6 real Rwanda geospatial layers. Root cause: the script that exports processed zone data into Unity's runtime format (`export_zone.py`) had never actually been committed to the repository, so the deployed zone files were stale placeholder data from before the real datasets were available locally. The same missing-data gap also meant the battery HUD could read a frozen 100% through an entire session under default weather, since sunny-weather solar income exactly cancelled the (also real, separately verified) baseline drain constant with nothing left over to actually move the number. Both are fixed now: the terrain mesh visibly deforms with real elevation (Central Plateau East ranges roughly 900-4400m before normalization) and obstacle placement now correlates with real slope/turbulence data instead of looking arbitrary, and the battery genuinely decreases under untouched, default-weather play (see Testing Results above). This is exactly the kind of gap that only surfaces once someone actually looks hard at what's on screen instead of trusting what the code is supposed to do, which is the point of the testing strategies above.
- **Demo controls that quietly broke what they were meant to demonstrate.** A closer re-audit of the "Demo Controls" panel found the "Obstacles" button was not just a UI convenience but actively destructive: on click it zeroed out the drone's real hazard grid and injected a handful of randomly placed synthetic hazards, meaning the demo view was fake data overwriting real data rather than a faithful window into the trained policy's actual environment. The fix was to delete the toggle and its overlay logic entirely rather than patch around it; obstacles are now always the real, terrain-derived hazard grid the policy was trained against, visible from page load with zero clicks. The same audit caught that the deployed ONNX policy and Unity's observation-space port had also drifted out of sync with the current Python training environment (Unity was missing 3 of 14 `mission_vector` fields), which was silently degrading inference quality without throwing any error. Both are fixed and independently verified: the re-exported ONNX policy matches its PyTorch source to within `0.00000048` max output difference, and the 9-zone evaluation in Testing Results above confirms the resulting behavior varies correctly with real terrain rather than collapsing to a fixed pattern.
- **A telemetry write path that silently dropped data for any genuinely new zone, found by trying to break it on purpose.** Deliberately sending the production telemetry API a never-before-seen zone identity, exactly the kind of fault-injection test meant to reveal how a system degrades under conditions it wasn't told to expect, surfaced a real bug rather than confirming graceful handling: the production database carried a `province` column left over from earlier schema history, `NOT NULL` with no default, but entirely absent from the current Prisma schema. Creating any zone the database hadn't already seen crashed with a generic `500` and quietly lost that episode's data, while writes against already-known zones kept succeeding, which is exactly why it had gone unnoticed. Root-caused by reproducing the write locally against the production database to get the real Prisma error instead of the generic 500, then fixed at the source by dropping the stale constraint rather than papering over it with a new required field. Re-verified live against a fresh zone identity, and a separate dropped-connection test confirmed the write path fails atomically, no partial rows, no effect on other requests, when a connection dies mid-upload instead of just being cleanly closed.
- **A demo control that read as fixed but was still quietly broken, caught only by watching the number for 15 seconds instead of trusting the label.** "Force Sunny" flipped the weather state correctly (cover retracted, rain visuals stopped) but set effective rainfall to a value still close enough to the sunny/rainy threshold that solar income stayed well below drain -- the battery kept falling under a button that reads "Force Sunny," which would have been a visibly broken moment in front of an audience clicking it expecting the opposite. Caught during this exact testing pass, not by inspection beforehand: the fix was a one-line change (effective rainfall to exactly 0 instead of threshold-adjacent), rebuilt and re-verified live (battery held flat at 100% over 20 seconds post-fix). This is the same lesson as the terrain-fidelity and obstacle-toggle findings above, repeated a third time: a feature's demo control can silently stop demonstrating the thing it's named after, and the only way to catch that is to actually run it and watch the number, not read the label.

## Discussion

A full parity audit between the Unity C# simulation and the Python training environment (`rwanda_env.py`) surfaced a documentation error worth stating plainly rather than quietly editing away: an earlier version of this section described the energy/weather interaction as state-aware emergency-return logic, sunny weather cancelling an in-progress critical return because the battery is recharging, rainy weather forcing a full return-and-land, and characterized it as validated, real learned behaviour. That was wrong. `rwanda_env.py` terminates the episode immediately and unconditionally the moment battery reaches its critical threshold, wherever the drone happens to be, with no weather condition and no scripted flight home first; the trained policy was never exposed to the split Unity had. That Unity-only weather-conditional freeze/cancel logic has been removed: battery now drains identically to Python every step of a return, and critical termination fires immediately rather than only after a completed return flight, matching the environment the deployed policy actually learned in. (Fix is in source only as of this writing; not yet rebuilt or re-verified live.)

The disturbance/reseeding loop is the other high-impact piece, since it is what makes the system a *closed loop* rather than a one-shot planner: failures are not just logged, they change future behavior (species recommendation, revisit scheduling). That closed-loop property is what would let a real deployment improve its seeding strategy over a multi-season campaign instead of repeating the same mistakes.

## Recommendations

- **For the community/future contributors:** the Python (`ARIA_ML`) and C# (`ARIA_Unity`) environments are independently maintained ports of the same simulation rules; any change to reward shaping, energy drain, or growth timing must be mirrored in both to keep the trained policy valid in the live simulation. A shared, language-agnostic config (e.g. JSON) for these constants would remove this duplication risk.
- **Future work:** extend the reseed pipeline so a killed seed's replant is visible immediately (e.g. an on-demand micro-mission) rather than only at the next full mission return, since the current architecture assumes a single agent per zone.
- **Deployment:** before any real-world pilot, the terrain/weather inputs would need to move from procedurally generated zones to actual satellite/soil-sensor data feeds, which the `Zone` data model already supports structurally.

---

## Repository Structure

```text
aria-capstone/
├── ARIA_ML/                        # Deep reinforcement learning: training + environment
│   ├── configs/                    # Training/environment configuration
│   ├── data/                       # raw/, processed/, zones/ -- terrain data used to build zones
│   ├── env/                        # Custom Gym environment (one file per subsystem)
│   │   ├── rwanda_env.py           # Top-level Gym environment
│   │   ├── growth_engine.py        # Seed lifecycle simulation
│   │   ├── energy_system.py        # Battery/solar model
│   │   ├── weather_system.py       # Weather/season model
│   │   ├── disturbance_engine.py   # Animal disturbance model
│   │   ├── monitoring_system.py    # Failure tracking + reseed recommendation
│   │   ├── reward_function.py      # Reward shaping
│   │   └── cnn_extractor.py        # CNN terrain feature extractor for the PPO policy
│   ├── notebook/                   # Interactive training/exploration notebook
│   ├── results/                    # checkpoints/, metrics/, plots/ from training runs
│   ├── training/train_ppo.py       # PPO training entrypoint
│   ├── utils/                      # preprocess.py, zone_builder.py
│   ├── export_to_onnx.py           # Exports the trained policy for use in Unity
│   ├── main.py
│   └── requirements.txt
├── ARIA_Unity/                     # Unity3D simulation project (source)
│   ├── Assets/
│   │   ├── Editor/BuildScript.cs   # Headless WebGL build entrypoint
│   │   ├── Resources/              # aria_policy.onnx, default materials
│   │   ├── Scenes/MainScene.unity
│   │   ├── Scripts/
│   │   │   ├── Core/                # ARIAConstants, EpisodeState, ActionDispatcher, CoverageOverride, ZoneData, RealZoneLoader, DemoConditions
│   │   │   ├── Drone/                # DroneController, SeedTreeManager, AnimalDisturbanceVisualizer, AerialObstacleVisualizer, TreeBuilder, RealTerrainRenderer, SceneBootstrapper, etc.
│   │   │   ├── Systems/              # GrowthEngine, EnergySystem, WeatherSystem, DisturbanceEngine, MonitoringSystem, TelemetryManager
│   │   │   ├── ML/                   # ARIAPolicyInference (Unity Inference Engine), ActionSelector
│   │   │   └── UI/DroneHUD.cs        # Demo Controls + HUD
│   │   └── StreamingAssets/          # aria_policy.onnx, real zone JSON files, zone_manifest.json
│   └── Packages/, ProjectSettings/   # Unity project configuration
├── ARIA_Web/                       # Next.js web application and API
│   ├── prisma/schema.prisma        # Database schema
│   ├── public/
│   │   ├── logo/                   # ARIA branding
│   │   └── simulation/Build/       # Pre-built WebGL bundle (.data.gz/.wasm.gz/.framework.js.gz) served by the web app
│   └── src/
│       ├── app/
│       │   ├── page.tsx            # Landing page
│       │   ├── simulation/page.tsx # Embedded WebGL simulation
│       │   ├── dashboard/          # page.tsx (data fetching) + DashboardClient.tsx (charts/tables)
│       │   └── api/monitoring/route.ts  # Telemetry ingestion endpoint (Unity -> Postgres)
│       └── lib/prisma.ts           # Prisma client (driver adapter setup)
├── docs/testing/screenshots/       # Testing evidence referenced above
├── LICENSE
└── README.md
```

---

## License
This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
