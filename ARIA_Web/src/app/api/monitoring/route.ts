import { NextResponse } from 'next/server';
import prisma from '@/lib/prisma';

export async function POST(request: Request) {
  try {
    const data = await request.json();
    const { zone, episode, seeds } = data;

    if (!zone || !episode || !seeds) {
      return NextResponse.json({ success: false, error: "Missing required data" }, { status: 400 });
    }

    // Find or create zone
    let dbZone = await prisma.zone.findFirst({
      where: { name: zone.name || "Default Zone" }
    });

    if (!dbZone) {
      dbZone = await prisma.zone.create({
        data: {
          name: zone.name || "Default Zone",
          agro_zone: zone.agro_zone || "Unknown",
        }
      });
    }

    // Create episode and seeds
    const dbEpisode = await prisma.episode.create({
      data: {
        zone_id: dbZone.zone_id,
        pct_suitable_seeded: episode.pct_suitable_seeded,
        spacing_violations: episode.spacing_violations,
        protected_area_seeds: episode.protected_area_seeds,
        reseeding_count: episode.reseeding_count,
        seeds: {
          create: seeds.map((s: any) => ({
            stage: s.stage,
            fail_reason: s.fail_reason || null,
            dropped_at_step: s.dropped_at_step,
          }))
        }
      }
    });

    return NextResponse.json({ success: true, episode_id: dbEpisode.episode_id });
  } catch (error) {
    console.error("Failed to save monitoring data:", error);
    return NextResponse.json({ success: false, error: "Internal Server Error" }, { status: 500 });
  }
}
