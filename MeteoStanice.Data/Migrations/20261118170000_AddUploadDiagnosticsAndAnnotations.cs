using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeteoStanice.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUploadDiagnosticsAndAnnotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ReceiveCount",
                table: "SensorSamples",
                type: "integer",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastReceivedAt",
                table: "SensorSamples",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SampleUploadBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReceivedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ProtocolVersion = table.Column<int>(type: "integer", nullable: false),
                    FirmwareVersion = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    BootId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReceivedCount = table.Column<int>(type: "integer", nullable: false),
                    StoredCount = table.Column<int>(type: "integer", nullable: false),
                    DuplicateCount = table.Column<int>(type: "integer", nullable: false),
                    RejectedCount = table.Column<int>(type: "integer", nullable: false),
                    FirstMeasuredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastMeasuredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MaxDelaySeconds = table.Column<double>(type: "double precision", nullable: true),
                    QueuePendingCount = table.Column<int>(type: "integer", nullable: true),
                    DroppedRamCount = table.Column<int>(type: "integer", nullable: true),
                    DroppedFlashCount = table.Column<int>(type: "integer", nullable: true),
                    StationRejectedCount = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SampleUploadBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SampleUploadBatches_Stations_StationId",
                        column: x => x.StationId,
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StationAnnotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    StationId = table.Column<Guid>(type: "uuid", nullable: true),
                    StartsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    EndsAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Category = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    AffectedChannels = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IsAnomaly = table.Column<bool>(type: "boolean", nullable: false),
                    Author = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StationAnnotations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StationAnnotations_Stations_StationId",
                        column: x => x.StationId,
                        principalTable: "Stations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SampleUploadBatches_StationId_ReceivedAt",
                table: "SampleUploadBatches",
                columns: new[] { "StationId", "ReceivedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_StationAnnotations_StartsAt",
                table: "StationAnnotations",
                column: "StartsAt");

            migrationBuilder.CreateIndex(
                name: "IX_StationAnnotations_StationId_StartsAt",
                table: "StationAnnotations",
                columns: new[] { "StationId", "StartsAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SampleUploadBatches");

            migrationBuilder.DropTable(
                name: "StationAnnotations");

            migrationBuilder.DropColumn(
                name: "ReceiveCount",
                table: "SensorSamples");

            migrationBuilder.DropColumn(
                name: "LastReceivedAt",
                table: "SensorSamples");
        }
    }
}
