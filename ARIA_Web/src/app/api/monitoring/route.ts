import { NextResponse } from 'next/server';
import prisma from '@/lib/prisma';

export async function POST(request: Request) {
  try {
    const data = await request.json();
    const { zone, episode, seeds, user_id } = data;

    if (!zone || !episode || !seeds) {
      return NextResponse.json({ success: false, error: "Missing required data" }, { status: 400 });
    }

    /* 0 is Unity's "no logged-in user" sentinel (see TelemetryManager.cs);
       only attach a user if it's a genuinely positive id AND actually exists,
       so a stale/tampered value can't crash the write with an FK violation. */
    let dbUserId: number | null = null;
    if (typeof user_id === "number" && user_id > 0) {
      const userExists = await prisma.user.findUnique({ where: { id: user_id }, select: { id: true } });
      if (userExists) dbUserId = user_id;
    }

    /* Find or create zone */
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

    /* Create episode and seeds */
    const dbEpisode = await prisma.episode.create({
      data: {
        zone_id: dbZone.zone_id,
        user_id: dbUserId,
        pct_suitable_seeded: episode.pct_suitable_seeded,
        spacing_violations: episode.spacing_violations,
        protected_area_seeds: episode.protected_area_seeds,
        reseeding_count: episode.reseeding_count,
        reward: episode.reward,
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
