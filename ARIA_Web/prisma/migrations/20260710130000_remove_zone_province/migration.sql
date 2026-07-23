-- Migration: remove_zone_province
--
-- Zone.province was always "Kigali" for every zone regardless of which
-- real zone was actually loaded (no real data source ever populated it
-- correctly), and it was never displayed anywhere on the dashboard.
-- Removed rather than kept as a placeholder.

ALTER TABLE "Zone" DROP COLUMN "province";
