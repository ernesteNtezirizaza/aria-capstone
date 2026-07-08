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
          province: zone.province || "Unknown",
          agro_zone: zone.agro_zone || "Unknown",
          area_km2: zone.area_km2 || 100.0,
          split_type: zone.split_type || "None"
        }
      });
    }

    // Create episode and seeds
    const dbEpisode = await prisma.episode.create({
      data: {
        zone_id: dbZone.zone_id,
        agent_type: episode.agent_type || "Unknown",
        total_reward: episode.total_reward,
        pct_suitable_seeded: episode.pct_suitable_seeded,
        mean_soil_score: episode.mean_soil_score,
        species_entropy: episode.species_entropy,
        spacing_violations: episode.spacing_violations,
        protected_area_seeds: episode.protected_area_seeds,
        n_seeds_placed: episode.n_seeds_placed,
        seeds: {
          create: seeds.map((s: any) => ({
            x_coord: s.x_coord,
            y_coord: s.y_coord,
            species_id: s.species_id,
            soil_score: s.soil_score,
            rain_score: s.rain_score,
            slope_score: s.slope_score,
            is_suitable: s.is_suitable,
            in_protected_area: s.in_protected_area,
            stage: s.stage,
            fail_reason: s.fail_reason || null,
            dropped_at: s.dropped_at,
            failed_at: s.failed_at >= 0 ? s.failed_at : null
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
