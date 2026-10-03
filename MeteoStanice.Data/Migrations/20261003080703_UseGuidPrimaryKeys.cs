using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeteoStanice.Data.Migrations
{
    /// <inheritdoc />
    public partial class UseGuidPrimaryKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE EXTENSION IF NOT EXISTS "pgcrypto";

                ALTER TABLE "Stations" ADD COLUMN "NewId" uuid;
                UPDATE "Stations" SET "NewId" = "StationGuid";
                ALTER TABLE "Stations" ALTER COLUMN "NewId" SET NOT NULL;

                ALTER TABLE "Measurements" ADD COLUMN "NewId" uuid NOT NULL DEFAULT gen_random_uuid();
                ALTER TABLE "Measurements" ADD COLUMN "NewStationId" uuid;
                UPDATE "Measurements" m
                SET "NewStationId" = s."NewId"
                FROM "Stations" s
                WHERE m."StationId" = s."Id";
                ALTER TABLE "Measurements" ALTER COLUMN "NewStationId" SET NOT NULL;

                ALTER TABLE "DeviceEvents" ADD COLUMN "NewId" uuid NOT NULL DEFAULT gen_random_uuid();
                ALTER TABLE "DeviceEvents" ADD COLUMN "NewStationId" uuid;
                UPDATE "DeviceEvents" e
                SET "NewStationId" = s."NewId"
                FROM "Stations" s
                WHERE e."StationId" = s."Id";
                ALTER TABLE "DeviceEvents" ALTER COLUMN "NewStationId" SET NOT NULL;

                ALTER TABLE "StationSettings" ADD COLUMN "NewId" uuid NOT NULL DEFAULT gen_random_uuid();
                ALTER TABLE "StationSettings" ADD COLUMN "NewStationId" uuid;
                UPDATE "StationSettings" ss
                SET "NewStationId" = s."NewId"
                FROM "Stations" s
                WHERE ss."StationId" = s."Id";
                ALTER TABLE "StationSettings" ALTER COLUMN "NewStationId" SET NOT NULL;

                ALTER TABLE "MeasurementQualityFlags" ADD COLUMN "NewId" uuid NOT NULL DEFAULT gen_random_uuid();
                ALTER TABLE "MeasurementQualityFlags" ADD COLUMN "NewMeasurementId" uuid;
                UPDATE "MeasurementQualityFlags" qf
                SET "NewMeasurementId" = m."NewId"
                FROM "Measurements" m
                WHERE qf."MeasurementId" = m."Id";
                ALTER TABLE "MeasurementQualityFlags" ALTER COLUMN "NewMeasurementId" SET NOT NULL;

                ALTER TABLE "MeasurementQualityFlags" DROP CONSTRAINT "FK_MeasurementQualityFlags_Measurements_MeasurementId";
                ALTER TABLE "Measurements" DROP CONSTRAINT "FK_Measurements_Stations_StationId";
                ALTER TABLE "DeviceEvents" DROP CONSTRAINT "FK_DeviceEvents_Stations_StationId";
                ALTER TABLE "StationSettings" DROP CONSTRAINT "FK_StationSettings_Stations_StationId";

                DROP INDEX "IX_MeasurementQualityFlags_MeasurementId_SensorChannelId_Quali~";
                DROP INDEX "IX_Measurements_StationId_MeasuredAt";
                DROP INDEX "IX_Measurements_StationId_SequenceNumber";
                DROP INDEX "IX_DeviceEvents_StationId_SequenceNumber";
                DROP INDEX "IX_StationSettings_StationId";

                ALTER TABLE "MeasurementQualityFlags" DROP CONSTRAINT "PK_MeasurementQualityFlags";
                ALTER TABLE "Measurements" DROP CONSTRAINT "PK_Measurements";
                ALTER TABLE "DeviceEvents" DROP CONSTRAINT "PK_DeviceEvents";
                ALTER TABLE "StationSettings" DROP CONSTRAINT "PK_StationSettings";
                ALTER TABLE "Stations" DROP CONSTRAINT "PK_Stations";

                ALTER TABLE "MeasurementQualityFlags" DROP COLUMN "MeasurementId";
                ALTER TABLE "MeasurementQualityFlags" DROP COLUMN "Id";
                ALTER TABLE "MeasurementQualityFlags" RENAME COLUMN "NewMeasurementId" TO "MeasurementId";
                ALTER TABLE "MeasurementQualityFlags" RENAME COLUMN "NewId" TO "Id";

                ALTER TABLE "Measurements" DROP COLUMN "StationId";
                ALTER TABLE "Measurements" DROP COLUMN "Id";
                ALTER TABLE "Measurements" RENAME COLUMN "NewStationId" TO "StationId";
                ALTER TABLE "Measurements" RENAME COLUMN "NewId" TO "Id";

                ALTER TABLE "DeviceEvents" DROP COLUMN "StationId";
                ALTER TABLE "DeviceEvents" DROP COLUMN "Id";
                ALTER TABLE "DeviceEvents" RENAME COLUMN "NewStationId" TO "StationId";
                ALTER TABLE "DeviceEvents" RENAME COLUMN "NewId" TO "Id";

                ALTER TABLE "StationSettings" DROP COLUMN "StationId";
                ALTER TABLE "StationSettings" DROP COLUMN "Id";
                ALTER TABLE "StationSettings" RENAME COLUMN "NewStationId" TO "StationId";
                ALTER TABLE "StationSettings" RENAME COLUMN "NewId" TO "Id";

                ALTER TABLE "Stations" DROP COLUMN "Id";
                ALTER TABLE "Stations" RENAME COLUMN "NewId" TO "Id";

                ALTER TABLE "Stations" ADD CONSTRAINT "PK_Stations" PRIMARY KEY ("Id");
                ALTER TABLE "Measurements" ADD CONSTRAINT "PK_Measurements" PRIMARY KEY ("Id");
                ALTER TABLE "DeviceEvents" ADD CONSTRAINT "PK_DeviceEvents" PRIMARY KEY ("Id");
                ALTER TABLE "StationSettings" ADD CONSTRAINT "PK_StationSettings" PRIMARY KEY ("Id");
                ALTER TABLE "MeasurementQualityFlags" ADD CONSTRAINT "PK_MeasurementQualityFlags" PRIMARY KEY ("Id");

                ALTER TABLE "Measurements" ADD CONSTRAINT "FK_Measurements_Stations_StationId"
                    FOREIGN KEY ("StationId") REFERENCES "Stations" ("Id") ON DELETE CASCADE;
                ALTER TABLE "DeviceEvents" ADD CONSTRAINT "FK_DeviceEvents_Stations_StationId"
                    FOREIGN KEY ("StationId") REFERENCES "Stations" ("Id") ON DELETE CASCADE;
                ALTER TABLE "StationSettings" ADD CONSTRAINT "FK_StationSettings_Stations_StationId"
                    FOREIGN KEY ("StationId") REFERENCES "Stations" ("Id") ON DELETE CASCADE;
                ALTER TABLE "MeasurementQualityFlags" ADD CONSTRAINT "FK_MeasurementQualityFlags_Measurements_MeasurementId"
                    FOREIGN KEY ("MeasurementId") REFERENCES "Measurements" ("Id") ON DELETE CASCADE;

                CREATE UNIQUE INDEX "IX_Measurements_StationId_SequenceNumber"
                    ON "Measurements" ("StationId", "SequenceNumber");
                CREATE INDEX "IX_Measurements_StationId_MeasuredAt"
                    ON "Measurements" ("StationId", "MeasuredAt");
                CREATE UNIQUE INDEX "IX_DeviceEvents_StationId_SequenceNumber"
                    ON "DeviceEvents" ("StationId", "SequenceNumber");
                CREATE UNIQUE INDEX "IX_StationSettings_StationId"
                    ON "StationSettings" ("StationId");
                CREATE INDEX "IX_MeasurementQualityFlags_MeasurementId_SensorChannelId_Quali~"
                    ON "MeasurementQualityFlags" ("MeasurementId", "SensorChannelId", "QualityCheckId");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Migration from GUID primary keys back to bigint identity keys is not supported safely.");
        }
    }
}
