'use client';

import {
  LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer, AreaChart, Area, BarChart, Bar, PieChart, Pie, Cell
} from 'recharts';
import { Activity, Target, AlertTriangle, Sprout, ArrowLeft, Database, Skull } from 'lucide-react';
import Link from 'next/link';

const STAGE_COLORS: Record<string, string> = {
  Dropped: '#94a3b8',
  Germinating: '#fbbf24',
  Seedling: '#34d399',
  Mature: '#059669',
  Dead: '#ef4444',
};

export default function DashboardClient({ episodes, stats, seedMonitoring }: { episodes: any[], stats: any, seedMonitoring?: { stageCounts: any[], recentFailures: any[] } }) {
  // Format data for charts
  const chartData = [...episodes].reverse().map((ep, idx) => ({
    name: `Ep ${ep.episode_id}`,
    reward: ep.total_reward || 0,
    suitable: (ep.pct_suitable_seeded || 0) * 100, // convert to percentage
    seeds: ep.n_seeds_placed || 0,
    violations: ep.spacing_violations || 0
  }));

  const stageData = (seedMonitoring?.stageCounts || [])
    .filter((s) => s.stage)
    .map((s) => ({ name: s.stage as string, value: s._count.stage as number }));
  const recentFailures = seedMonitoring?.recentFailures || [];

  return (
    <div className="max-w-7xl mx-auto px-4 py-8">
      {/* Header */}
      <header className="flex flex-col md:flex-row items-start md:items-center justify-between mb-10 gap-4">
        <div>
          <Link href="/" className="inline-flex items-center text-sm text-indigo-500 hover:text-indigo-400 mb-2 transition-colors">
            <ArrowLeft className="w-4 h-4 mr-1" /> Back to Home
          </Link>
          <h1 className="text-3xl font-bold tracking-tight">System Monitoring</h1>
          <p className="text-slate-500 dark:text-slate-400 mt-1">Real-time telemetrics from the ARIA simulation</p>
        </div>
        <div className="flex items-center gap-2 px-4 py-2 rounded-full bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 text-sm font-medium border border-emerald-500/20">
          <div className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
          Live Connection
        </div>
      </header>

      {/* KPI Cards */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-6 mb-10">
        <StatCard 
          title="Total Episodes" 
          value={stats.totalEpisodes.toLocaleString()} 
          icon={<Activity className="w-5 h-5 text-blue-500" />}
          trend="+12% from last hour"
        />
        <StatCard 
          title="Total Seeds Placed" 
          value={stats.totalSeeds.toLocaleString()} 
          icon={<Sprout className="w-5 h-5 text-emerald-500" />}
          trend="Across all zones"
        />
        <StatCard 
          title="Avg Model Reward" 
          value={stats.avgReward.toFixed(2)} 
          icon={<Target className="w-5 h-5 text-indigo-500" />}
          trend="PPO Agent Performance"
        />
        <StatCard 
          title="Avg Suitable %" 
          value={`${(stats.avgSuitable * 100).toFixed(1)}%`} 
          icon={<AlertTriangle className="w-5 h-5 text-amber-500" />}
          trend="Accuracy metric"
        />
      </div>

      {/* Charts */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-10">
        {/* Reward Chart */}
        <div className="p-6 rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-sm">
          <h3 className="text-lg font-semibold mb-6 flex items-center gap-2">
            <Activity className="w-5 h-5 text-indigo-500" />
            Total Reward over Time
          </h3>
          <div className="h-72">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={chartData}>
                <defs>
                  <linearGradient id="colorReward" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="5%" stopColor="#6366f1" stopOpacity={0.3}/>
                    <stop offset="95%" stopColor="#6366f1" stopOpacity={0}/>
                  </linearGradient>
                </defs>
                <CartesianGrid strokeDasharray="3 3" stroke="#334155" opacity={0.2} vertical={false} />
                <XAxis dataKey="name" stroke="#64748b" fontSize={12} tickLine={false} axisLine={false} />
                <YAxis stroke="#64748b" fontSize={12} tickLine={false} axisLine={false} />
                <Tooltip 
                  contentStyle={{ backgroundColor: '#0f172a', borderColor: '#1e293b', color: '#f8fafc', borderRadius: '8px' }}
                  itemStyle={{ color: '#818cf8' }}
                />
                <Area type="monotone" dataKey="reward" stroke="#6366f1" strokeWidth={3} fillOpacity={1} fill="url(#colorReward)" />
              </AreaChart>
            </ResponsiveContainer>
          </div>
        </div>

        {/* Suitable Seeds Chart */}
        <div className="p-6 rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-sm">
          <h3 className="text-lg font-semibold mb-6 flex items-center gap-2">
            <Target className="w-5 h-5 text-emerald-500" />
            Suitable Seeding %
          </h3>
          <div className="h-72">
            <ResponsiveContainer width="100%" height="100%">
              <LineChart data={chartData}>
                <CartesianGrid strokeDasharray="3 3" stroke="#334155" opacity={0.2} vertical={false} />
                <XAxis dataKey="name" stroke="#64748b" fontSize={12} tickLine={false} axisLine={false} />
                <YAxis stroke="#64748b" fontSize={12} tickLine={false} axisLine={false} domain={[0, 100]} />
                <Tooltip 
                  contentStyle={{ backgroundColor: '#0f172a', borderColor: '#1e293b', color: '#f8fafc', borderRadius: '8px' }}
                  itemStyle={{ color: '#34d399' }}
                />
                <Line type="monotone" dataKey="suitable" stroke="#10b981" strokeWidth={3} dot={{ r: 4, strokeWidth: 2 }} activeDot={{ r: 6 }} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        </div>

        {/* Spacing Violations Chart */}
        <div className="p-6 rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-sm lg:col-span-2">
          <h3 className="text-lg font-semibold mb-6 flex items-center gap-2">
            <AlertTriangle className="w-5 h-5 text-amber-500" />
            Spacing Violations per Episode
          </h3>
          <div className="h-64">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={chartData}>
                <CartesianGrid strokeDasharray="3 3" stroke="#334155" opacity={0.2} vertical={false} />
                <XAxis dataKey="name" stroke="#64748b" fontSize={12} tickLine={false} axisLine={false} />
                <YAxis stroke="#64748b" fontSize={12} tickLine={false} axisLine={false} />
                <Tooltip 
                  contentStyle={{ backgroundColor: '#0f172a', borderColor: '#1e293b', color: '#f8fafc', borderRadius: '8px' }}
                  cursor={{ fill: '#334155', opacity: 0.1 }}
                />
                <Bar dataKey="violations" fill="#f59e0b" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
        </div>
      </div>

      {/* Seed Monitoring */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-10">
        {/* Stage breakdown */}
        <div className="p-6 rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-sm">
          <h3 className="text-lg font-semibold mb-6 flex items-center gap-2">
            <Sprout className="w-5 h-5 text-emerald-500" />
            Seed Lifecycle Breakdown
          </h3>
          {stageData.length > 0 ? (
            <div className="h-64">
              <ResponsiveContainer width="100%" height="100%">
                <PieChart>
                  <Pie data={stageData} dataKey="value" nameKey="name" cx="50%" cy="50%" innerRadius={50} outerRadius={90} paddingAngle={2}>
                    {stageData.map((entry, idx) => (
                      <Cell key={idx} fill={STAGE_COLORS[entry.name] || '#6366f1'} />
                    ))}
                  </Pie>
                  <Legend />
                  <Tooltip contentStyle={{ backgroundColor: '#0f172a', borderColor: '#1e293b', color: '#f8fafc', borderRadius: '8px' }} />
                </PieChart>
              </ResponsiveContainer>
            </div>
          ) : (
            <div className="h-64 flex items-center justify-center text-slate-500 text-sm">No seed lifecycle data yet.</div>
          )}
        </div>

        {/* Recent failures */}
        <div className="p-6 rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-sm overflow-hidden">
          <h3 className="text-lg font-semibold mb-6 flex items-center gap-2">
            <Skull className="w-5 h-5 text-red-500" />
            Recent Failures &amp; Reseed Targets
          </h3>
          <div className="overflow-x-auto -mx-2 px-2 max-h-64 overflow-y-auto">
            <table className="w-full text-sm text-left whitespace-nowrap">
              <thead className="text-xs text-slate-500 uppercase bg-slate-50 dark:bg-slate-800/50 sticky top-0">
                <tr>
                  <th className="px-3 py-2 font-medium">Seed</th>
                  <th className="px-3 py-2 font-medium">Zone</th>
                  <th className="px-3 py-2 font-medium">Cell</th>
                  <th className="px-3 py-2 font-medium">Reason</th>
                  <th className="px-3 py-2 font-medium">Failed @ Step</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
                {recentFailures.map((s: any) => (
                  <tr key={s.seed_id} className="hover:bg-slate-50 dark:hover:bg-slate-800/30 transition-colors">
                    <td className="px-3 py-2 font-medium">#{s.seed_id}</td>
                    <td className="px-3 py-2">{s.episode?.zone?.name || 'Unknown'}</td>
                    <td className="px-3 py-2 font-mono text-xs">({s.x_coord}, {s.y_coord})</td>
                    <td className="px-3 py-2">
                      <span className="px-2 py-0.5 rounded-full bg-red-50 dark:bg-red-500/10 text-red-600 dark:text-red-400 text-xs font-medium">
                        {s.fail_reason || 'unknown'}
                      </span>
                    </td>
                    <td className="px-3 py-2 text-slate-500">{s.failed_at ?? 'N/A'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
            {recentFailures.length === 0 && (
              <div className="p-8 text-center text-slate-500 text-sm">No seed failures recorded yet.</div>
            )}
          </div>
        </div>
      </div>

      {/* Recent Episodes Table */}
      <div className="p-4 sm:p-6 rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-sm overflow-hidden">
        <h3 className="text-lg font-semibold mb-6 flex items-center gap-2">
          <Database className="w-5 h-5 text-slate-400" />
          Recent Episodes Log
        </h3>
        <div className="overflow-x-auto -mx-4 sm:mx-0 px-4 sm:px-0">
          <table className="w-full text-sm text-left whitespace-nowrap">
            <thead className="text-xs text-slate-500 uppercase bg-slate-50 dark:bg-slate-800/50">
              <tr>
                <th className="px-3 sm:px-6 py-3 sm:py-4 font-medium rounded-tl-lg">Episode ID</th>
                <th className="px-3 sm:px-6 py-3 sm:py-4 font-medium">Zone</th>
                <th className="px-3 sm:px-6 py-3 sm:py-4 font-medium">Agent Type</th>
                <th className="px-3 sm:px-6 py-3 sm:py-4 font-medium">Reward</th>
                <th className="px-3 sm:px-6 py-3 sm:py-4 font-medium rounded-tr-lg">Time</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
              {episodes.slice(0, 10).map((ep) => (
                <tr key={ep.episode_id} className="hover:bg-slate-50 dark:hover:bg-slate-800/30 transition-colors">
                  <td className="px-3 sm:px-6 py-3 sm:py-4 font-medium">#{ep.episode_id}</td>
                  <td className="px-3 sm:px-6 py-3 sm:py-4">{ep.zone?.name || 'Unknown'}</td>
                  <td className="px-3 sm:px-6 py-3 sm:py-4">
                    <span className="px-2.5 py-1 rounded-full bg-slate-100 dark:bg-slate-800 text-xs font-medium">
                      {ep.agent_type}
                    </span>
                  </td>
                  <td className="px-3 sm:px-6 py-3 sm:py-4 font-mono">{ep.total_reward?.toFixed(2) || 'N/A'}</td>
                  <td className="px-3 sm:px-6 py-3 sm:py-4 text-slate-500">
                    {new Date(ep.timestamp).toLocaleTimeString('en-US', { timeZone: 'UTC', hour: '2-digit', minute: '2-digit' })}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
          {episodes.length === 0 && (
            <div className="p-12 text-center text-slate-500">
              No simulation data received yet. Run the Unity simulation to see live data.
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function StatCard({ title, value, icon, trend }: { title: string, value: string, icon: React.ReactNode, trend: string }) {
  return (
    <div className="p-6 rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-sm relative overflow-hidden group">
      <div className="absolute top-0 right-0 p-6 opacity-20 group-hover:scale-110 transition-transform duration-500 group-hover:opacity-30">
        {icon}
      </div>
      <div className="flex items-center gap-3 mb-4">
        <div className="p-2 rounded-xl bg-slate-50 dark:bg-slate-800 border border-slate-100 dark:border-slate-700">
          {icon}
        </div>
        <h3 className="text-sm font-medium text-slate-500 dark:text-slate-400">{title}</h3>
      </div>
      <div className="text-3xl font-bold mb-1 tracking-tight">{value}</div>
      <div className="text-xs font-medium text-slate-400">{trend}</div>
    </div>
  );
}
