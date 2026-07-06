# ARIA: Adaptive Reforestation Intelligence Agent

> **An autonomous, AI-driven drone seeding simulation and monitoring platform for reforestation efforts.**

![ARIA System Overview](ARIA_Web/public/logo/logo.png)

---

## Technical Walkthrough Video

> **[Click here to watch the full technical walkthrough video on YouTube](#)** *(Replace with actual video link)*

---

## Project Overview

**ARIA (Adaptive Reforestation Intelligence Agent)** seamlessly bridges advanced Machine Learning with a high-fidelity Unity-based simulation to monitor, learn, and execute autonomous drone seeding operations across diverse environments. 

Our mission is to combat deforestation by deploying deep reinforcement learning (DRL) algorithms that determine optimal seed placement based on complex environmental factors such as soil quality, slope, and rain patterns.

---

## System Architecture

ARIA utilizes a highly decoupled, distributed architecture consisting of three primary components:

### 1. `ARIA_ML` (PPO + CNN Agent)
The core intelligence of the system. We utilize **Proximal Policy Optimization (PPO)** combined with **Convolutional Neural Networks (CNN)** to process spatial terrain data. 
- **Tech Stack:** Python, PyTorch, ONNX
- **Functionality:** Analyzes topographic features and makes complex seeding decisions to maximize the survival rate of the planted seeds.

### 2. `ARIA_Unity` (Physics Simulation)
A robust physics-based simulation environment that models drone flight dynamics and environmental interactions.
- **Tech Stack:** Unity3D, C#, ML-Agents
- **Functionality:** Executes the policy determined by the ML model. It records exact seed coordinates, monitors drone battery levels, and simulates wind and terrain obstacles in real-time.

### 3. `ARIA_Web` (Real-Time Telemetry & Monitoring)
The command center for the ARIA system.
- **Tech Stack:** Next.js (React), Tailwind CSS, Prisma, Neon PostgreSQL, Recharts
- **Functionality:** A responsive, dark-themed dashboard that ingests real-time telemetrics via REST APIs. It provides rich visualizations of episode rewards, suitable seeding percentages, and historical drone performance.

---

## Installation & Setup Instructions

Follow these steps to run the ARIA ecosystem locally on your machine.

### Prerequisites
- [Node.js (v18+)](https://nodejs.org/) & npm
- [Python 3.9+](https://www.python.org/)
- [Unity Hub & Unity Editor (2022.3 LTS or newer)](https://unity.com/)
- [Git](https://git-scm.com/)

### Step 1: Clone the Repository
```bash
git clone https://github.com/ernesteNtezirizaza/aria-capstone.git
cd aria-capstone
```

### Step 2: Set up the ARIA_Web Dashboard
The web dashboard acts as the telemetry receiver. It requires a PostgreSQL database (we recommend Neon DB).

```bash
cd ARIA_Web
npm install
```

1. Create a `.env` file in the `ARIA_Web` directory.
2. Add your PostgreSQL connection string:
   ```env
   DATABASE_URL="postgresql://user:password@host/dbname?sslmode=require"
   ```
3. Run database migrations to set up the schema:
   ```bash
   npx prisma db push
   ```
4. Start the development server:
   ```bash
   npm run dev
   ```
The dashboard will now be running at `http://localhost:3000`.

### Step 3: Set up the ARIA_ML Environment
```bash
cd ../ARIA_ML
python -m venv venv
# On Windows: venv\Scripts\activate
# On Mac/Linux: source venv/bin/activate

pip install -r requirements.txt
```
*(Optional)* To initiate a new training session, you can run the PPO training script:
```bash
python training/train_ppo.py
```
Alternatively, you can run and explore the model training process interactively via the Jupyter Notebook:
```bash
jupyter notebook notebook/aria_notebook.ipynb
```

### Step 4: Run the ARIA_Unity Simulation
1. Open **Unity Hub**.
2. Click **Add** and select the `aria-capstone/ARIA_Unity` directory.
3. Open the project in the Unity Editor.
4. Load the `MainSimulation` scene located in `Assets/Scenes/`.
5. Ensure the API endpoint in the `TelemetryManager` script is pointing to `http://localhost:3000/api/monitoring`.
6. Press the **Play** button in the Unity Editor. 

You should now see the drone executing its seeding patterns in Unity, while live data populates in your web dashboard!

---

## Repository Structure

```text
aria-capstone/
├── ARIA_ML/               # Deep Reinforcement Learning models and training scripts
│   ├── models/            # PyTorch models and ONNX exports
│   └── training/          # PPO + CNN training loops and configurations
├── ARIA_Unity/            # Unity3D simulation project
│   ├── Assets/            # Scripts, prefabs, materials, and scenes
│   └── Packages/          # Unity package dependencies (ML-Agents)
├── ARIA_Web/              # Next.js web application and API
│   ├── prisma/            # Database schema and migrations
│   ├── public/            # Static assets and ARIA branding
│   └── src/               # React components, dashboard UI, and API routes
├── .gitignore             # Root gitignore rules
└── README.md              # Project documentation
```

---

## Contributing
As this is a capstone project, contributions are closed for the academic grading period. However, feel free to fork the repository for your own research in autonomous drone operations and reinforcement learning.

## License
This project is licensed under the MIT License. See the `LICENSE` file for details.
