import prisma from '@/lib/prisma';
import DashboardClient from './DashboardClient';

export const dynamic = 'force-dynamic';

export default async function DashboardPage() {
  let episodes: Awaited<ReturnType<typeof prisma.episode.findMany>> = [];
  let totalEpisodes = 0;
  let totalSeeds = 0;
  let stageCounts: { stage: string; count: number }[] = [];
  let recentFailures: Awaited<ReturnType<typeof prisma.seed.findMany>> = [];
  let dataUnavailable = false;

  try {
    episodes = await prisma.episode.findMany({
      orderBy: { episode_id: 'desc' },
      take: 50,
      include: {
        zone: true,
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

  // Calculate average suitable seeded percentage
  const episodesWithSuitable = episodes.filter((e: any) => e.pct_suitable_seeded !== null);
  const avgSuitable = episodesWithSuitable.length > 0
    ? episodesWithSuitable.reduce((acc: number, curr: any) => acc + (curr.pct_suitable_seeded || 0), 0) / episodesWithSuitable.length
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
          avgSuitable
        }}
        seedMonitoring={{
          stageCounts,
          recentFailures
        }}
      />
    </div>
  );
}
