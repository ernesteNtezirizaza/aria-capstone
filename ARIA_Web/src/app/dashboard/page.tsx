import prisma from '@/lib/prisma';
import DashboardClient from './DashboardClient';

export const dynamic = 'force-dynamic';

export default async function DashboardPage() {
  const episodes = await prisma.episode.findMany({
    orderBy: { timestamp: 'desc' },
    take: 50,
    include: {
      zone: true,
      _count: {
        select: { seeds: true }
      }
    }
  });

  const totalEpisodes = await prisma.episode.count();
  const totalSeeds = await prisma.seed.count();

  // Seed-monitoring: lifecycle stage breakdown + recent failures for the reseed pipeline.
  const stageCounts = await prisma.seed.groupBy({
    by: ['stage'],
    _count: { stage: true },
  });

  const recentFailures = await prisma.seed.findMany({
    where: { stage: 'Dead' },
    orderBy: { seed_id: 'desc' },
    take: 15,
    include: { episode: { include: { zone: true } } }
  });

  // Calculate average reward
  const episodesWithReward = episodes.filter((e: any) => e.total_reward !== null);
  const avgReward = episodesWithReward.length > 0
    ? episodesWithReward.reduce((acc: number, curr: any) => acc + (curr.total_reward || 0), 0) / episodesWithReward.length
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
        stats={{
          totalEpisodes,
          totalSeeds,
          avgReward,
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
