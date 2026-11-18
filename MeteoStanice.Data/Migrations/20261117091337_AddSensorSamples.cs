using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeteoStanice.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSensorSamples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SensorSamples",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StationId = table.Column<Guid>(type: "uuid", nullable: false),
                    SequenceNumber = table.Column<long>(type: "bigint", nullable: false),
                    MeasuredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TemperatureCelsius = table.Column<double>(type: "double precision", nullable: true),
                    HumidityPercent = table.Column<double>(type: "double precision", nullable: true),
                    PressureHpa = table.Column<double>(type: "double precision", nullable: true),
                    Bmp280TemperatureCelsius = table.Column<double>(type: "double precision", nullable: true),
                    Pm1 = table.Column<double>(type: "double precision", nullable: true),
                    Pm25 = table.Column<double>(type: "double precision", nullable: true),
                    Pm4 = table.Column<double>(type: "double precision", nullable: true),
                    Pm10 = table.Column<double>(type: "double precision", nullable: true),
                    TypicalParticleSize = table.Column<double>(type: "double precision", nullable: true),
                    BatteryVoltage = table.Column<double>(type: "double precision", nullable: true),
                    BatteryLow = table.Column<bool>(type: "boolean", nullable: false),
                    PowerSource = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Sht40Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Bmp280Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Sps30Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SensorSamples", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SensorSamples_Stations_StationId",
                        column: x => x.StationId,
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SensorSamples_MeasuredAt",
                table: "SensorSamples",
                column: "MeasuredAt");

            migrationBuilder.CreateIndex(
                name: "IX_SensorSamples_StationId_MeasuredAt",
                table: "SensorSamples",
                columns: new[] { "StationId", "MeasuredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SensorSamples_StationId_SequenceNumber",
                table: "SensorSamples",
                columns: new[] { "StationId", "SequenceNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SensorSamples");
        }
    }
}
