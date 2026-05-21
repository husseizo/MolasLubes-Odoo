ALTER TABLE "NeonLiquiMolyProducts"
    ADD COLUMN IF NOT EXISTS "PrimaryBarcode" character varying(50) NULL;

ALTER TABLE "NeonLiquiMolyProducts"
    ADD COLUMN IF NOT EXISTS "PrimaryBarcodeUomCode" character varying(20) NULL;

ALTER TABLE "NeonLiquiMolyProducts"
    ADD COLUMN IF NOT EXISTS "PrimaryBarcodeUomName" character varying(100) NULL;

ALTER TABLE "NeonLiquiMolyProducts"
    ADD COLUMN IF NOT EXISTS "PrimaryBarcodeUomEntry" integer NULL;

ALTER TABLE "NeonLiquiMolyProducts"
    ADD COLUMN IF NOT EXISTS "PrimaryBarcodeBaseQtyInGroup" numeric(19,6) NULL;

ALTER TABLE "NeonLiquiMolyProducts"
    ADD COLUMN IF NOT EXISTS "HasUnitBarcode" boolean NOT NULL DEFAULT false;

ALTER TABLE "NeonLiquiMolyProducts"
    ADD COLUMN IF NOT EXISTS "BarcodeResolutionStatus" character varying(50) NULL;

ALTER TABLE "NeonLiquiMolyProducts"
    ADD COLUMN IF NOT EXISTS "BarcodeResolutionNote" text NULL;

ALTER TABLE "NeonLiquiMolyProducts"
    ADD COLUMN IF NOT EXISTS "AllBarcodes" text NULL;

ALTER TABLE "NeonLiquiMolyProducts"
    ADD COLUMN IF NOT EXISTS "SapUomInfo" text NULL;
