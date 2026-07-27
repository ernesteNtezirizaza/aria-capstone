import prisma from '@/lib/prisma';
import { getSession } from '@/lib/auth';
import DashboardShell from '@/components/DashboardShell';
import { Skull } from 'lucide-react';

export const dynamic = 'force-dynamic';

export default async function FailuresPage() {
  const session = await getSession();
  const isAdmin = session?.role === 'ADMIN';
  const isForester = session?.role === 'FORESTER';
  const userId = session ? Number(session.sub) : null;

  let recentFailures: Awaited<ReturnType<typeof prisma.seed.findMany>> = [];
  let dataUnavailable = false;

  try {
    recentFailures = await prisma.seed.findMany({
      where: {
        stage: 'Dead',
        ...(isForester && userId != null ? { episode: { user_id: userId } } : {}),
      },
      orderBy: { seed_id: 'desc' },
      take: 50,
      include: { episode: { include: { zone: true, user: { select: { name: true } } } } },
    });
  } catch (error) {
    console.error('Failures data fetch failed:', error);
    dataUnavailable = true;
  }

  return (
    <DashboardShell
      session={session ? { name: session.name, role: session.role } : null}
      title="Failures & Reseed Targets"
      subtitle={
        isForester
          ? 'Every seed of yours that died, why, and which reseed target it fed into.'
          : 'Every seed that died, why, and which reseed target it fed into -- all users.'
      }
      dataUnavailable={dataUnavailable}
    >
      <div className="p-4 sm:p-6 rounded-2xl bg-white dark:bg-slate-900 border border-slate-200 dark:border-slate-800 shadow-sm overflow-hidden">
        <h3 className="text-lg font-semibold mb-6 flex items-center gap-2">
          <Skull className="w-5 h-5 text-red-500" />
          Recent Failures &amp; Reseed Targets
        </h3>
        <div className="overflow-x-auto -mx-4 sm:mx-0 px-4 sm:px-0">
          <table className="w-full text-sm text-left whitespace-nowrap">
            <thead className="text-xs text-slate-500 uppercase bg-slate-50 dark:bg-slate-800/50">
              <tr>
                <th className="px-3 sm:px-6 py-3 font-medium rounded-tl-lg">Seed</th>
                <th className="px-3 sm:px-6 py-3 font-medium">Zone</th>
                {isAdmin && <th className="px-3 sm:px-6 py-3 font-medium">User</th>}
                <th className="px-3 sm:px-6 py-3 font-medium">Reason</th>
                <th className="px-3 sm:px-6 py-3 font-medium rounded-tr-lg">Dropped At Step</th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200 dark:divide-slate-800">
              {recentFailures.map((s: any) => (
                <tr key={s.seed_id} className="hover:bg-slate-50 dark:hover:bg-slate-800/30 transition-colors">
                  <td className="px-3 sm:px-6 py-3 font-medium">#{s.seed_id}</td>
                  <td className="px-3 sm:px-6 py-3">{s.episode?.zone?.name || 'Unknown'}</td>
                  {isAdmin && (
                    <td className="px-3 sm:px-6 py-3 text-slate-500 dark:text-slate-400">
                      {s.episode?.user?.name || '—'}
                    </td>
                  )}
                  <td className="px-3 sm:px-6 py-3">
                    <span className="px-2 py-0.5 rounded-full bg-red-50 dark:bg-red-500/10 text-red-600 dark:text-red-400 text-xs font-medium">
                      {s.fail_reason || 'unknown'}
                    </span>
                  </td>
                  <td className="px-3 sm:px-6 py-3 text-slate-400 font-mono text-xs">{s.dropped_at_step ?? 'N/A'}</td>
                </tr>
              ))}
            </tbody>
          </table>
          {recentFailures.length === 0 && (
            <div className="p-12 text-center text-slate-500 text-sm">No seed failures recorded yet.</div>
          )}
        </div>
      </div>
    </DashboardShell>
  );
}
