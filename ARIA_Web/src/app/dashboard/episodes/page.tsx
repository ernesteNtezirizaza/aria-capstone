import prisma from '@/lib/prisma';
import { getSession } from '@/lib/auth';
import DashboardShell from '@/components/DashboardShell';
import { Database } from 'lucide-react';

export const dynamic = 'force-dynamic';

export default async function EpisodesPage() {
  const session = await getSession();
  const isAdmin = session?.role === 'ADMIN';
  const isForester = session?.role === 'FORESTER';
  const userId = session ? Number(session.sub) : null;

  let episodes: Awaited<ReturnType<typeof prisma.episode.findMany>> = [];
  let dataUnavailable = false;

  try {
    episodes = await prisma.episode.findMany({
      where: isForester && userId != null ? { user_id: userId } : {},
      orderBy: { episode_id: 'desc' },
      take: 100,
      include: { zone: true, user: { select: { name: true } }, _count: { select: { seeds: true } } },
    });
  } catch (error) {
    console.error('Episodes data fetch failed:', error);
    dataUnavailable = true;
  }

  return (
    <DashboardShell
      session={session ? { name: session.name, role: session.role } : null}
      title="Recent Episodes Log"
      subtitle={
        isForester
          ? 'Every episode you have run.'
          : 'Every episode the live simulation has reported -- all users.'
      }
      dataUnavailable={dataUnavailable}
    >
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
                {isAdmin && <th className="px-3 sm:px-6 py-3 sm:py-4 font-medium">User</th>}
                <th className="px-3 sm:px-6 py-3 sm:py-4 font-medium">Suitable %</th>
                <th className="px-3 sm:px-6 py-3 sm:py-4 font-medium">Spacing Violations</th>
                <th className="px-3 sm:px-6 py-3 sm:py-4 font-medium">Reseeding Count</th>
                <th className="px-3 sm:px-6 py-3 sm:py-4 font-medium rounded-tr-lg">Reward</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
              {episodes.map((ep: any) => (
                <tr key={ep.episode_id} className="hover:bg-slate-50 dark:hover:bg-slate-800/30 transition-colors">
                  <td className="px-3 sm:px-6 py-3 sm:py-4 font-medium">#{ep.episode_id}</td>
                  <td className="px-3 sm:px-6 py-3 sm:py-4">{ep.zone?.name || 'Unknown'}</td>
                  {isAdmin && (
                    <td className="px-3 sm:px-6 py-3 sm:py-4 text-slate-500 dark:text-slate-400">
                      {ep.user?.name || '—'}
                    </td>
                  )}
                  <td className="px-3 sm:px-6 py-3 sm:py-4 font-mono">
                    {ep.pct_suitable_seeded != null ? `${(ep.pct_suitable_seeded * 100).toFixed(1)}%` : 'N/A'}
                  </td>
                  <td className="px-3 sm:px-6 py-3 sm:py-4 font-mono">{ep.spacing_violations ?? 'N/A'}</td>
                  <td className="px-3 sm:px-6 py-3 sm:py-4 font-mono">{ep.reseeding_count ?? 'N/A'}</td>
                  <td className="px-3 sm:px-6 py-3 sm:py-4 font-mono">{ep.reward != null ? ep.reward.toFixed(1) : 'N/A'}</td>
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
    </DashboardShell>
  );
}
