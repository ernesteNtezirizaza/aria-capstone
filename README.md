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
The core intelligence of the system. We use **Proximal Policy Optimization (PPO)** combined with a **Convolutional Neural Network (CNN)** terrain extractor.
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
- **Functional/manual testing**: exercising each of the 7 core features via the in-sim "Demo Controls" panel, which forces specific conditions (weather, obstacles, disturbance, zone) on demand rather than waiting for them to occur naturally.
- **Boundary/edge-case testing**: forcing battery to its critical threshold under both rainy and sunny weather to verify the two different emergency-response paths (return-and-land vs. keep-seeding-while-recharging).
- **Data variation testing**: ran multiple episodes across different zones (`Central Plateau East`, `Northern Plateau East`) to confirm terrain, seed counts, and reward calculations vary correctly with zone data.

### Evidence

**Landing Page**
![Home](docs/testing/screenshots/01-home.png)

**Unified Planner & Navigator**: the drone autonomously sweeps the zone, selecting species per cell based on terrain suitability:
![Seeding](docs/testing/screenshots/04-simulation-seeding.png)

**Obstacle Detection & Avoidance**: hazards (red markers) spawn dynamically and the drone routes around them:
![Obstacles](docs/testing/screenshots/05-simulation-obstacles.png)

**Rain Cover Mechanism**: cover deploys automatically the moment rainfall is detected:
![Rain Cover](docs/testing/screenshots/06-simulation-rain-cover.png)

**Animal Disturbance**: goats are introduced into the zone and begin killing nearby seeds/trees, which schedules a reseed target:
![Animal Disturbance](docs/testing/screenshots/07-simulation-animal-disturbance.png)

**Zone Switching**: instantly reloads a different zone's terrain, seed budget, and protected-area layout:
![Zone Switch](docs/testing/screenshots/09-simulation-zone-switch.png)

**Real-Time Telemetry Dashboard**: live episode history, reward trends, spacing violations, seed lifecycle breakdown, and recent failure/reseed targets:
![Dashboard](docs/testing/screenshots/02-dashboard-overview.png)

---

## Analysis

The project's central objective, an autonomous agent that plans *and* navigates a reforestation mission end-to-end while adapting to live environmental disturbance, was achieved. The deployed system demonstrates all seven planned functionalities running against a live PPO+CNN policy, not scripted animations: the dashboard's `1,182` seeds placed and `96.42` average reward across `12` episodes are computed from real inference runs, not fixture data.

One objective was partially rather than fully met:
- **Reseed pipeline visibility.** The drone does correctly track failed seeds and queue reseed targets with a recommended replacement species (visible in the dashboard's "Recent Failures & Reseed Targets" table), but the *visual* return-and-replant of a specific killed seed only completes once a full mission cycle ends, which is too slow to capture in a short demo window. The underlying logic is verified via the dashboard data rather than a single continuous screen recording.

## Discussion

The energy/weather interaction (Step "Mission Abort & Return-to-Base") turned out to be the most architecturally significant milestone: it forced the emergency-return logic to be state-aware rather than a single threshold check: sunny weather cancels an in-progress critical return because the battery is recharging, while rainy weather forces a full return-and-land. Getting this right matters beyond the demo: it is the difference between a drone that strands itself mid-mission and one that behaves safely under real solar/weather variability, which is the actual operating condition a field-deployed reforestation drone would face.

The disturbance/reseeding loop is the other high-impact piece, since it is what makes the system a *closed loop* rather than a one-shot planner: failures are not just logged, they change future behavior (species recommendation, revisit scheduling). That closed-loop property is what would let a real deployment improve its seeding strategy over a multi-season campaign instead of repeating the same mistakes.

## Recommendations

- **For the community/future contributors:** the Python (`ARIA_ML`) and C# (`ARIA_Unity`) environments are independently maintained ports of the same simulation rules; any change to reward shaping, energy drain, or growth timing must be mirrored in both to keep the trained policy valid in the live simulation. A shared, language-agnostic config (e.g. JSON) for these constants would remove this duplication risk.
- **Future work:** extend the reseed pipeline so a killed seed's replant is visible immediately (e.g. an on-demand micro-mission) rather than only at the next full mission return, since the current architecture assumes a single agent per zone.
- **Deployment:** before any real-world pilot, the terrain/weather inputs would need to move from procedurally generated zones to actual satellite/soil-sensor data feeds, which the `Zone` data model already supports structurally.

---

## Repository Structure

```text
aria-capstone/
├── ARIA_ML/               # Deep Reinforcement Learning models and training scripts
│   ├── env/               # Custom Gym environment (terrain, energy, weather, growth, disturbance)
│   ├── training/           # PPO + CNN training loop and configuration
│   └── export_to_onnx.py  # Exports the trained policy for use in Unity
├── ARIA_Unity/             # Unity3D simulation project (source)
│   └── Assets/Scripts/     # Core sim (C# port of ARIA_ML/env), Drone, Systems, UI
├── ARIA_Web/               # Next.js web application and API
│   ├── prisma/             # Database schema
│   ├── public/simulation/  # Pre-built WebGL bundle served by the web app
│   └── src/                # React components, dashboard UI, and API routes
├── docs/testing/screenshots/  # Testing evidence referenced above
├── LICENSE
└── README.md
```

---

## Contributing
As this is a capstone project, contributions are closed for the academic grading period. However, feel free to fork the repository for your own research in autonomous drone operations and reinforcement learning.

## License
This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.
