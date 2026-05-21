ALTER TABLE "NeonLiquiMolyProducts"
    ADD COLUMN IF NOT EXISTS "Application" text NULL;

ALTER TABLE "NeonLiquiMolyProducts"
    ADD COLUMN IF NOT EXISTS "LiquiMolyRecommendations" text NULL;

ALTER TABLE "NeonLiquiMolyProducts"
    ADD COLUMN IF NOT EXISTS "SpecificationItems" text NULL;
