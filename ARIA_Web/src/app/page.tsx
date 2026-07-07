'use client';

import { useState } from 'react';
import Link from 'next/link';
import Image from 'next/image';
import { ArrowRight, Activity, Database, Zap, TreePine, Cpu, Network, Menu, X } from 'lucide-react';

export default function LandingPage() {
  const [mobileMenuOpen, setMobileMenuOpen] = useState(false);

  return (
    <div className="min-h-screen bg-slate-950 text-slate-50 selection:bg-emerald-500/30">
      {/* Background decoration */}
      <div className="absolute inset-0 overflow-hidden pointer-events-none">
        <div className="absolute top-0 left-1/2 -translate-x-1/2 w-[1000px] h-[500px] opacity-20 bg-[radial-gradient(ellipse_at_top,_var(--tw-gradient-stops))] from-emerald-500 via-slate-900 to-transparent blur-3xl"></div>
        
        {/* Tree decorative elements */}
        <div className="absolute top-20 left-0 right-0 h-full flex justify-between items-end opacity-[0.03] overflow-hidden pointer-events-none">
          <TreePine className="w-48 h-48 md:w-96 md:h-96 -ml-20 -mb-20 text-emerald-400" />
          <TreePine className="w-60 h-60 md:w-[30rem] md:h-[30rem] absolute left-1/4 -mb-32 text-emerald-400" />
          <TreePine className="w-72 h-72 md:w-[40rem] md:h-[40rem] absolute right-1/4 -mb-40 text-emerald-400" />
          <TreePine className="w-64 h-64 md:w-[35rem] md:h-[35rem] -mr-32 -mb-24 text-emerald-400" />
        </div>
      </div>

      {/* Navbar */}
      <nav className="fixed top-0 left-0 right-0 z-50 bg-slate-950/80 backdrop-blur-md border-b border-slate-800">
        <div className="flex items-center justify-between px-4 sm:px-8 py-4 max-w-7xl mx-auto w-full">
          <div className="flex items-center shrink-0">
            <Image src="/logo/logo.png" alt="ARIA Logo" width={240} height={80} className="object-contain h-10 w-auto sm:h-12 md:h-14" priority />
          </div>

          {/* Desktop links */}
          <div className="hidden md:flex items-center gap-6 text-sm font-medium text-slate-300">
            <Link href="#features" className="hover:text-white transition-colors">Features</Link>
            <Link href="#architecture" className="hover:text-white transition-colors">Architecture</Link>
            <a href="/simulation/index.html" className="hover:text-white transition-colors">Simulation</a>
            <Link href="/dashboard" className="px-4 py-2 rounded-full bg-white/10 hover:bg-white/20 text-white transition-all backdrop-blur-md">
              Go to Dashboard
            </Link>
          </div>

          {/* Mobile menu toggle */}
          <button
            type="button"
            onClick={() => setMobileMenuOpen((open) => !open)}
            className="md:hidden inline-flex items-center justify-center p-2 rounded-lg text-slate-300 hover:text-white hover:bg-white/10 transition-colors"
            aria-label="Toggle navigation menu"
            aria-expanded={mobileMenuOpen}
          >
            {mobileMenuOpen ? <X className="w-6 h-6" /> : <Menu className="w-6 h-6" />}
          </button>
        </div>

        {/* Mobile dropdown */}
        {mobileMenuOpen && (
          <div className="md:hidden border-t border-slate-800 bg-slate-950/95 backdrop-blur-md px-4 py-4 flex flex-col gap-4 text-sm font-medium text-slate-300">
            <Link href="#features" onClick={() => setMobileMenuOpen(false)} className="hover:text-white transition-colors">Features</Link>
            <Link href="#architecture" onClick={() => setMobileMenuOpen(false)} className="hover:text-white transition-colors">Architecture</Link>
            <a href="/simulation/index.html" onClick={() => setMobileMenuOpen(false)} className="hover:text-white transition-colors">Simulation</a>
            <Link href="/dashboard" onClick={() => setMobileMenuOpen(false)} className="px-4 py-2 rounded-full bg-white/10 hover:bg-white/20 text-white transition-all backdrop-blur-md text-center">
              Go to Dashboard
            </Link>
          </div>
        )}
      </nav>

      {/* Hero Section */}
      <main className="relative z-10 flex flex-col items-center justify-center text-center px-4 pt-32 pb-20 max-w-5xl mx-auto">
        <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-emerald-500/10 border border-emerald-500/20 text-emerald-400 text-sm font-medium mb-8 animate-fade-in-up">
          <span className="relative flex h-2 w-2">
            <span className="animate-ping absolute inline-flex h-full w-full rounded-full bg-emerald-400 opacity-75"></span>
            <span className="relative inline-flex rounded-full h-2 w-2 bg-emerald-500"></span>
          </span>
          System Live
        </div>
        
        <h1 className="text-5xl md:text-7xl font-bold tracking-tight mb-8 leading-tight">
          Intelligent Terrain <br/>
          <span className="text-transparent bg-clip-text bg-gradient-to-r from-emerald-400 to-cyan-400">
            Monitoring & Seeding
          </span>
        </h1>
        
        <p className="text-lg md:text-xl text-slate-400 max-w-2xl mb-12 leading-relaxed">
          ARIA seamlessly bridges advanced Machine Learning with Unity-based simulation 
          to monitor and execute autonomous drone seeding operations across diverse environments.
        </p>
        
        <div className="flex flex-col sm:flex-row items-center gap-4">
          <Link href="/dashboard" className="group flex items-center gap-2 px-8 py-4 rounded-full bg-emerald-600 hover:bg-emerald-500 text-white font-medium transition-all shadow-xl shadow-emerald-500/20">
            View Live Dashboard
            <ArrowRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
          </Link>
          <a href="/simulation/index.html" className="group flex items-center gap-2 px-8 py-4 rounded-full bg-white/5 hover:bg-white/10 text-white font-medium transition-all backdrop-blur-sm border border-white/10">
            View Simulation
            <ArrowRight className="w-4 h-4 group-hover:translate-x-1 transition-transform" />
          </a>
          <a href="#features" className="px-8 py-4 rounded-full bg-white/5 hover:bg-white/10 text-white font-medium transition-all backdrop-blur-sm border border-white/10">
            Learn More
          </a>
        </div>
      </main>

      {/* Features Section */}
      <section id="features" className="relative z-10 py-24 px-4 max-w-7xl mx-auto grid grid-cols-1 md:grid-cols-3 gap-8">
        <div className="p-8 rounded-3xl bg-slate-900/50 border border-slate-800 backdrop-blur-sm hover:bg-slate-800/50 transition-colors group">
          <div className="w-12 h-12 rounded-2xl bg-blue-500/10 flex items-center justify-center text-blue-400 mb-6 group-hover:scale-110 transition-transform">
            <Activity className="w-6 h-6" />
          </div>
          <h3 className="text-xl font-semibold mb-3 text-slate-200">Real-time Monitoring</h3>
          <p className="text-slate-400 leading-relaxed">
            Track drone episodes, seed placement, and terrain statistics instantly as the Unity simulation runs.
          </p>
        </div>
        
        <div className="p-8 rounded-3xl bg-slate-900/50 border border-slate-800 backdrop-blur-sm hover:bg-slate-800/50 transition-colors group">
          <div className="w-12 h-12 rounded-2xl bg-emerald-500/10 flex items-center justify-center text-emerald-400 mb-6 group-hover:scale-110 transition-transform">
            <Database className="w-6 h-6" />
          </div>
          <h3 className="text-xl font-semibold mb-3 text-slate-200">Persistent Data</h3>
          <p className="text-slate-400 leading-relaxed">
            All simulation data is securely persisted in Neon PostgreSQL, allowing historical performance analysis.
          </p>
        </div>

        <div className="p-8 rounded-3xl bg-slate-900/50 border border-slate-800 backdrop-blur-sm hover:bg-slate-800/50 transition-colors group">
          <div className="w-12 h-12 rounded-2xl bg-cyan-500/10 flex items-center justify-center text-cyan-400 mb-6 group-hover:scale-110 transition-transform">
            <Zap className="w-6 h-6" />
          </div>
          <h3 className="text-xl font-semibold mb-3 text-slate-200">ML Integration</h3>
          <p className="text-slate-400 leading-relaxed">
            The dashboard displays the efficacy of the PPO-CNN agent, tracking suitable seed placement percentages and rewards.
          </p>
        </div>
      </section>

      {/* Architecture Section */}
      <section id="architecture" className="relative z-10 py-24 px-4 max-w-7xl mx-auto border-t border-slate-800/50">
        <div className="text-center mb-16">
          <h2 className="text-3xl md:text-4xl font-bold tracking-tight mb-4">System Architecture</h2>
          <p className="text-slate-400 max-w-2xl mx-auto">ARIA utilizes a distributed architecture to coordinate ML training, Unity simulation, and real-time telemetrics.</p>
        </div>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-8">
          <div className="p-8 rounded-3xl bg-slate-900/50 border border-slate-800 backdrop-blur-sm hover:bg-slate-800/50 transition-colors group">
            <div className="w-12 h-12 rounded-2xl bg-indigo-500/10 flex items-center justify-center text-indigo-400 mb-6 group-hover:scale-110 transition-transform">
              <Cpu className="w-6 h-6" />
            </div>
            <h3 className="text-xl font-semibold mb-3">PPO + CNN Agent (Python)</h3>
            <p className="text-slate-400">A Proximal Policy Optimization model combined with Convolutional Neural Networks processes spatial terrain data and makes intelligent seeding decisions based on soil, slope, and rain patterns.</p>
          </div>
          <div className="p-8 rounded-3xl bg-slate-900/50 border border-slate-800 backdrop-blur-sm hover:bg-slate-800/50 transition-colors group">
            <div className="w-12 h-12 rounded-2xl bg-emerald-500/10 flex items-center justify-center text-emerald-400 mb-6 group-hover:scale-110 transition-transform">
              <Network className="w-6 h-6" />
            </div>
            <h3 className="text-xl font-semibold mb-3">Unity Simulation</h3>
            <p className="text-slate-400">Physics-based simulation models the drone flight and records exact seed coordinates and environmental conditions.</p>
          </div>
          <div className="p-8 rounded-3xl bg-slate-900/50 border border-slate-800 backdrop-blur-sm hover:bg-slate-800/50 transition-colors group">
            <div className="w-12 h-12 rounded-2xl bg-cyan-500/10 flex items-center justify-center text-cyan-400 mb-6 group-hover:scale-110 transition-transform">
              <Database className="w-6 h-6" />
            </div>
            <h3 className="text-xl font-semibold mb-3">Next.js & Neon DB</h3>
            <p className="text-slate-400">This web application visualizes the data received via REST APIs, persisting episodes to a serverless Postgres database.</p>
          </div>
        </div>
      </section>

      {/* Footer */}
      <footer className="relative z-10 py-8 px-4 border-t border-slate-800/50 mt-12 bg-slate-950">
        <div className="max-w-7xl mx-auto flex items-center justify-center">
          <div className="text-slate-500 text-sm text-center">
            &copy; {new Date().getFullYear()} ARIA. Adaptive Reforestation Intelligence Agent. All rights reserved.
          </div>
        </div>
      </footer>
    </div>
  );
}
