-- Migration: add_real_timestamps_to_seed
--
-- Seed.dropped_at / Seed.failed_at were Int (a simulation timestep), not a
-- real timestamp. This migration is written to be NON-DESTRUCTIVE: it
-- renames the existing integer columns instead of dropping them, then adds
-- new DateTime columns for the real wall-clock time.
--
-- Why rename instead of just changing the type: Postgres cannot
-- automatically cast an integer step count into a TIMESTAMP (there's no
-- meaningful conversion), so a naive type change would force dropping and
-- recreating the column -- destroying every existing dropped_at/failed_at
-- value in your database. Renaming preserves that historical data under
-- its new, more honest name.

-- 1. Preserve existing integer step data under its new name.
ALTER TABLE "Seed" RENAME COLUMN "dropped_at" TO "dropped_at_step";
ALTER TABLE "Seed" RENAME COLUMN "failed_at" TO "failed_at_step";

-- 2. Add the new real-timestamp columns. These will be NULL for every
--    existing row (there is no way to reconstruct a real wall-clock time
--    for historical events that were only ever recorded as a simulation
--    step) and will be populated going forward once the updated Unity
--    build starts sending dropped_at / failed_at as ISO 8601 strings
--    (see TelemetryManager.cs and api/monitoring/route.ts).
ALTER TABLE "Seed" ADD COLUMN "dropped_at" TIMESTAMP(3);
ALTER TABLE "Seed" ADD COLUMN "failed_at" TIMESTAMP(3);
