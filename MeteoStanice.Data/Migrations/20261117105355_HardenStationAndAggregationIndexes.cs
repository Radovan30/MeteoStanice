using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeteoStanice.Data.Migrations
{
    /// <inheritdoc />
    public partial class HardenStationAndAggregationIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                WITH ranked AS (
                    SELECT "Id",
                           ROW_NUMBER() OVER (
                               PARTITION BY "HardwareId"
                               ORDER BY "CreatedAt", "Id") AS row_number
                    FROM "Stations"
                    WHERE "HardwareId" <> ''
                )
                UPDATE "Stations" AS station
                SET "HardwareId" = ''
                FROM ranked
                WHERE station."Id" = ranked."Id"
                  AND ranked.row_number > 1;
                """);

            migrationBuilder.Sql(
                """
                DELETE FROM "MeasurementQualityFlags" AS duplicate
                USING "MeasurementQualityFlags" AS retained
                WHERE duplicate."MeasurementId" = retained."MeasurementId"
                  AND duplicate."SensorChannelId" = retained."SensorChannelId"
                  AND duplicate."QualityCheckId" = retained."QualityCheckId"
                  AND duplicate."Id" > retained."Id";
                """);

            migrationBuilder.DropIndex(
                name: "IX_MeasurementQualityFlags_MeasurementId_SensorChannelId_Quali~",
                table: "MeasurementQualityFlags");

            migrationBuilder.CreateIndex(
                name: "IX_Stations_HardwareId",
                table: "Stations",
                column: "HardwareId",
                unique: true,
                filter: "\"HardwareId\" <> ''");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementQualityFlags_MeasurementId_SensorChannelId_Quali~",
                table: "MeasurementQualityFlags",
                columns: new[] { "MeasurementId", "SensorChannelId", "QualityCheckId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEvents_StationId_OccurredAt",
                table: "DeviceEvents",
                columns: new[] { "StationId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Stations_HardwareId",
                table: "Stations");

            migrationBuilder.DropIndex(
                name: "IX_MeasurementQualityFlags_MeasurementId_SensorChannelId_Quali~",
                table: "MeasurementQualityFlags");

            migrationBuilder.DropIndex(
                name: "IX_DeviceEvents_StationId_OccurredAt",
                table: "DeviceEvents");

            migrationBuilder.CreateIndex(
                name: "IX_MeasurementQualityFlags_MeasurementId_SensorChannelId_Quali~",
                table: "MeasurementQualityFlags",
                columns: new[] { "MeasurementId", "SensorChannelId", "QualityCheckId" });
        }
    }
}
