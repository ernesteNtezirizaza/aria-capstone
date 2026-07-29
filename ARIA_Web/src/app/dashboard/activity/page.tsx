import { redirect } from 'next/navigation';
import prisma from '@/lib/prisma';
import { getSession } from '@/lib/auth';
import DashboardShell from '@/components/DashboardShell';
import { BarChart3 } from 'lucide-react';

export const dynamic = 'force-dynamic';

export default async function ActivityPage() {
  const session = await getSession();
  if (!session || session.role !== 'ADMIN') redirect('/dashboard');

  let perUserStats: { userId: number; name: string; email: string; episodeCount: number }[] = [];
  let dataUnavailable = false;

  try {
    /* Aggregated in JS rather than Prisma groupBy -- groupBy on a nullable
       column (user_id) errors against the pg driver adapter used here. */
    const taggedEpisodes = await prisma.episode.findMany({
      where: { user_id: { not: null } },
      select: { user_id: true, user: { select: { name: true, email: true } } },
    });
    const countMap = new Map<number, { name: string; email: string; episodeCount: number }>();
    for (const ep of taggedEpisodes) {
      if (ep.user_id == null || !ep.user) continue;
      const existing = countMap.get(ep.user_id);
      if (existing) {
        existing.episodeCount += 1;
      } else {
        countMap.set(ep.user_id, { name: ep.user.name, email: ep.user.email, episodeCount: 1 });
      }
    }
    perUserStats = Array.from(countMap.entries())
      .map(([userId, v]) => ({ userId, ...v }))
      .sort((a, b) => b.episodeCount - a.episodeCount);
  } catch (error) {
    console.error('User activity data fetch failed:', error);
    dataUnavailable = true;
  }

  return (
    <DashboardShell
      session={{ name: session.name, role: session.role }}
      title="Simulation Activity by User"
      subtitle="Which logged-in users have actually run the simulation."
      dataUnavailable={dataUnavailable}
    >
      <div className="p-4 sm:p-6 rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-sm overflow-hidden">
        <h3 className="text-lg font-semibold mb-6 flex items-center gap-2">
          <BarChart3 className="w-5 h-5 text-cyan-500" />
          Simulation Activity by User
        </h3>
        <div className="overflow-x-auto -mx-4 sm:mx-0 px-4 sm:px-0">
          <table className="w-full text-sm text-left whitespace-nowrap">
            <thead className="text-xs text-slate-500 uppercase bg-slate-50 dark:bg-slate-800/50">
              <tr>
                <th className="px-3 sm:px-6 py-3 font-medium rounded-tl-lg">User</th>
                <th className="px-3 sm:px-6 py-3 font-medium">Email</th>
                <th className="px-3 sm:px-6 py-3 font-medium rounded-tr-lg">Episodes Run</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
              {perUserStats.map((u) => (
                <tr key={u.userId} className="hover:bg-slate-50 dark:hover:bg-slate-800/30 transition-colors">
                  <td className="px-3 sm:px-6 py-3 font-medium">{u.name}</td>
                  <td className="px-3 sm:px-6 py-3 text-slate-400">{u.email}</td>
                  <td className="px-3 sm:px-6 py-3 font-mono">{u.episodeCount}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {perUserStats.length === 0 && (
            <div className="p-12 text-center text-slate-500 text-sm">
              No simulation runs have been tagged to a user yet.
            </div>
          )}
        </div>
      </div>
    </DashboardShell>
  );
}
