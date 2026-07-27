'use client';

import {
  LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer, AreaChart, Area, BarChart, Bar, PieChart, Pie, Cell
} from 'recharts';
import { Activity, Target, AlertTriangle, Sprout, CloudOff } from 'lucide-react';
import DashboardShell from '@/components/DashboardShell';

type SessionInfo = { name: string; role: 'ADMIN' | 'FORESTER' } | null;

const STAGE_COLORS: Record<string, string> = {
  Dropped: '#94a3b8',
  Germinating: '#fbbf24',
  Seedling: '#34d399',
  Mature: '#059669',
  Dead: '#ef4444',
};

export default function DashboardClient({
  episodes,
  stats,
  seedMonitoring,
  dataUnavailable,
  session,
}: {
  episodes: any[];
  stats: any;
  seedMonitoring?: { stageCounts: any[] };
  dataUnavailable?: boolean;
  session?: SessionInfo;
}) {
  // Format data for charts
  const chartData = [...episodes].reverse().map((ep) => ({
    name: `Ep ${ep.episode_id}`,
    reseeding: ep.reseeding_count || 0,
    suitable: (ep.pct_suitable_seeded || 0) * 100, // convert to percentage
    violations: ep.spacing_violations || 0
  }));

  const stageData = (seedMonitoring?.stageCounts || [])
    .filter((s) => s.stage && s.stage !== 'Unknown')
    .map((s) => ({ name: s.stage as string, value: s.count as number }));

  return (
    <DashboardShell
      session={session}
      title="System Monitoring"
      subtitle="Real-time telemetrics from the ARIA simulation"
      dataUnavailable={dataUnavailable}
      headerRight={
        dataUnavailable ? (
          <div className="flex items-center gap-2 px-4 py-2 rounded-full bg-amber-500/10 text-amber-600 dark:text-amber-400 text-sm font-medium border border-amber-500/20">
            <CloudOff className="w-4 h-4" />
            No Data Available
          </div>
        ) : (
          <div className="flex items-center gap-2 px-4 py-2 rounded-full bg-emerald-500/10 text-emerald-600 dark:text-emerald-400 text-sm font-medium border border-emerald-500/20">
            <div className="w-2 h-2 rounded-full bg-emerald-500 animate-pulse" />
            Live Connection
          </div>
        )
      }
    >
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
          title="Avg Reseeding Count"
          value={stats.avgReseedingCount.toFixed(2)}
          icon={<Target className="w-5 h-5 text-indigo-500" />}
          trend="Reseed pipeline activity"
        />
        <StatCard
          title="Avg Reward"
          value={stats.avgReward.toFixed(1)}
          icon={<AlertTriangle className="w-5 h-5 text-amber-500" />}
          trend="Trained-policy reward per episode"
        />
      </div>

      {/* Charts */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 mb-10">
        {/* Reseeding Chart */}
        <div className="p-6 rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-sm">
          <h3 className="text-lg font-semibold mb-6 flex items-center gap-2">
            <Activity className="w-5 h-5 text-indigo-500" />
            Reseeding Count over Time
          </h3>
          <div className="h-72">
            <ResponsiveContainer width="100%" height="100%">
              <AreaChart data={chartData}>
                <defs>
                  <linearGradient id="colorReseeding" x1="0" y1="0" x2="0" y2="1">
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
                <Area type="monotone" dataKey="reseeding" stroke="#6366f1" strokeWidth={3} fillOpacity={1} fill="url(#colorReseeding)" />
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

      {/* Seed Lifecycle Breakdown */}
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
    </DashboardShell>
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
