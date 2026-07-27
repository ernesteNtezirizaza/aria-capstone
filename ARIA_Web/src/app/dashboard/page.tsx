import prisma from '@/lib/prisma';
import { getSession } from '@/lib/auth';
import DashboardClient from './DashboardClient';

export const dynamic = 'force-dynamic';

export default async function DashboardPage() {
  const session = await getSession();
  const isAdmin = session?.role === 'ADMIN';

  let episodes: Awaited<ReturnType<typeof prisma.episode.findMany>> = [];
  let totalEpisodes = 0;
  let totalSeeds = 0;
  let stageCounts: { stage: string; count: number }[] = [];
  let recentFailures: Awaited<ReturnType<typeof prisma.seed.findMany>> = [];
  let perUserStats: { userId: number; name: string; email: string; episodeCount: number }[] = [];
  let dataUnavailable = false;

  try {
    episodes = await prisma.episode.findMany({
      orderBy: { episode_id: 'desc' },
      take: 50,
      include: {
        zone: true,
        user: { select: { name: true, email: true } },
        _count: {
          select: { seeds: true }
        }
      }
    });

    totalEpisodes = await prisma.episode.count();
    totalSeeds = await prisma.seed.count();

    // Seed-monitoring: lifecycle stage breakdown + recent failures for the reseed pipeline.
    // Aggregated in JS rather than via Prisma groupBy -- groupBy on a nullable column
    // errored against the pg driver adapter used here (poisoned the whole request).
    const allStages = await prisma.seed.findMany({ select: { stage: true } });
    const stageCountMap: Record<string, number> = {};
    for (const s of allStages) {
      const key = s.stage || 'Unknown';
      stageCountMap[key] = (stageCountMap[key] || 0) + 1;
    }
    stageCounts = Object.entries(stageCountMap).map(([stage, count]) => ({ stage, count }));

    recentFailures = await prisma.seed.findMany({
      where: { stage: 'Dead' },
      orderBy: { seed_id: 'desc' },
      take: 15,
      include: { episode: { include: { zone: true } } }
    });

    // Per-user simulation attribution -- admin-only visibility, since a
    // Forester's own dashboard view isn't meant to show who else ran what.
    // Aggregated in JS rather than Prisma groupBy -- groupBy on a nullable
    // column (user_id) hits the same pg-adapter issue noted above for
    // seed.stage.
    if (isAdmin) {
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
    }
  } catch (error) {
    // A DB/table-level failure here (e.g. a missing table) shouldn't crash
    // the whole page -- fall back to an empty, honestly-labeled dashboard
    // instead of an unhandled 500.
    console.error('Dashboard data fetch failed:', error);
    dataUnavailable = true;
  }

  // Calculate average reseeding count
  const episodesWithReseeding = episodes.filter((e: any) => e.reseeding_count !== null);
  const avgReseedingCount = episodesWithReseeding.length > 0
    ? episodesWithReseeding.reduce((acc: number, curr: any) => acc + (curr.reseeding_count || 0), 0) / episodesWithReseeding.length
    : 0;

  // Calculate average episode reward -- the real trained-policy reward
  // (ActionDispatcher.Step()/reward_function.py parity), not a training-only
  // metric anymore now that Unity computes and reports it per episode.
  const episodesWithReward = episodes.filter((e: any) => e.reward !== null);
  const avgReward = episodesWithReward.length > 0
    ? episodesWithReward.reduce((acc: number, curr: any) => acc + (curr.reward || 0), 0) / episodesWithReward.length
    : 0;

  return (
    <div className="min-h-screen bg-slate-50 dark:bg-slate-950 text-slate-900 dark:text-slate-50">
      <DashboardClient
        episodes={episodes}
        dataUnavailable={dataUnavailable}
        stats={{
          totalEpisodes,
          totalSeeds,
          avgReseedingCount,
          avgReward
        }}
        seedMonitoring={{
          stageCounts,
          recentFailures
        }}
        session={session ? { name: session.name, role: session.role } : null}
        perUserStats={isAdmin ? perUserStats : undefined}
      />
    </div>
  );
}
