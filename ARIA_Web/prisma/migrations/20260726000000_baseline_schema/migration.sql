-- Baseline migration.
--
-- Production had never actually been managed through Prisma Migrate --
-- schema changes were applied via `db push` and, most recently, direct
-- manual SQL (matching Zone/Episode/Seed to the approved ER diagram:
-- dropped area_km2/split_type/agent_type/total_reward/mean_soil_score/
-- species_entropy/timestamp/n_seeds_placed/x_coord/y_coord/species_id/
-- soil_score/rain_score/slope_score/is_suitable/in_protected_area/
-- failed_at_step/dropped_at/failed_at/province, added reseeding_count),
-- since `prisma db push` couldn't reach the pooled Neon connection
-- (P1001). No `_prisma_migrations` table existed at all before this.
-- This migration's CREATE TABLE statements match the live schema exactly
-- and are marked as already-applied via `prisma migrate resolve
-- --applied` rather than re-run, since the tables already exist in this
-- shape. Two now-inaccurate migrations from an earlier, superseded schema
-- (20260710120000_add_real_timestamps_to_seed,
-- 20260710130000_remove_zone_province) were removed rather than kept,
-- since replaying them against the current schema would fail outright
-- (they reference columns -- dropped_at, failed_at, province -- that no
-- longer exist).

-- CreateSchema
CREATE SCHEMA IF NOT EXISTS "public";

-- CreateTable
CREATE TABLE "Zone" (
    "zone_id" SERIAL NOT NULL,
    "name" TEXT NOT NULL,
    "agro_zone" TEXT NOT NULL,

    CONSTRAINT "Zone_pkey" PRIMARY KEY ("zone_id")
);

-- CreateTable
CREATE TABLE "Episode" (
    "episode_id" SERIAL NOT NULL,
    "zone_id" INTEGER NOT NULL,
    "pct_suitable_seeded" DOUBLE PRECISION,
    "spacing_violations" INTEGER,
    "protected_area_seeds" INTEGER,
    "reseeding_count" INTEGER,

    CONSTRAINT "Episode_pkey" PRIMARY KEY ("episode_id")
);

-- CreateTable
CREATE TABLE "Seed" (
    "seed_id" SERIAL NOT NULL,
    "episode_id" INTEGER NOT NULL,
    "stage" TEXT,
    "fail_reason" TEXT,
    "dropped_at_step" INTEGER,

    CONSTRAINT "Seed_pkey" PRIMARY KEY ("seed_id")
);

-- AddForeignKey
ALTER TABLE "Episode" ADD CONSTRAINT "Episode_zone_id_fkey" FOREIGN KEY ("zone_id") REFERENCES "Zone"("zone_id") ON DELETE RESTRICT ON UPDATE CASCADE;

-- AddForeignKey
ALTER TABLE "Seed" ADD CONSTRAINT "Seed_episode_id_fkey" FOREIGN KEY ("episode_id") REFERENCES "Episode"("episode_id") ON DELETE RESTRICT ON UPDATE CASCADE;

