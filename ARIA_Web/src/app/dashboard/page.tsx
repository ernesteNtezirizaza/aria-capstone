import prisma from '@/lib/prisma';
import { getSession } from '@/lib/auth';
import DashboardClient from './DashboardClient';

export const dynamic = 'force-dynamic';

export default async function DashboardPage() {
  const session = await getSession();
  const isForester = session?.role === 'FORESTER';
  const userId = session ? Number(session.sub) : null;

  /* Foresters only ever see their own simulation runs; Admins see
     everything (attributed by user full name where it's shown per-row). */
  const episodeFilter = isForester && userId != null ? { user_id: userId } : {};
  const seedFilter = isForester && userId != null ? { episode: { user_id: userId } } : {};

  let episodes: Awaited<ReturnType<typeof prisma.episode.findMany>> = [];
  let totalEpisodes = 0;
  let totalSeeds = 0;
  let stageCounts: { stage: string; count: number }[] = [];
  let dataUnavailable = false;

  try {
    episodes = await prisma.episode.findMany({
      where: episodeFilter,
      orderBy: { episode_id: 'desc' },
      take: 50,
      include: {
        zone: true,
        user: { select: { name: true } },
        _count: {
          select: { seeds: true }
        }
      }
    });

    totalEpisodes = await prisma.episode.count({ where: episodeFilter });
    totalSeeds = await prisma.seed.count({ where: seedFilter });

    /* Seed lifecycle stage breakdown for the pie chart. Aggregated in JS
       rather than via Prisma groupBy -- groupBy on a nullable column errored
       against the pg driver adapter used here (poisoned the whole request). */
    const allStages = await prisma.seed.findMany({ where: seedFilter, select: { stage: true } });
    const stageCountMap: Record<string, number> = {};
    for (const s of allStages) {
      const key = s.stage || 'Unknown';
      stageCountMap[key] = (stageCountMap[key] || 0) + 1;
    }
    stageCounts = Object.entries(stageCountMap).map(([stage, count]) => ({ stage, count }));
  } catch (error) {
    /* A DB/table-level failure here (e.g. a missing table) shouldn't crash
       the whole page -- fall back to an empty, honestly-labeled dashboard
       instead of an unhandled 500. */
    console.error('Dashboard data fetch failed:', error);
    dataUnavailable = true;
  }

  /* Calculate average reseeding count */
  const episodesWithReseeding = episodes.filter((e: any) => e.reseeding_count !== null);
  const avgReseedingCount = episodesWithReseeding.length > 0
    ? episodesWithReseeding.reduce((acc: number, curr: any) => acc + (curr.reseeding_count || 0), 0) / episodesWithReseeding.length
    : 0;

  /* Calculate average episode reward -- the real trained-policy reward
     (ActionDispatcher.Step()/reward_function.py parity), not a training-only
     metric anymore now that Unity computes and reports it per episode. */
  const episodesWithReward = episodes.filter((e: any) => e.reward !== null);
  const avgReward = episodesWithReward.length > 0
    ? episodesWithReward.reduce((acc: number, curr: any) => acc + (curr.reward || 0), 0) / episodesWithReward.length
    : 0;

  return (
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
        stageCounts
      }}
      session={session ? { name: session.name, role: session.role } : null}
    />
  );
}
