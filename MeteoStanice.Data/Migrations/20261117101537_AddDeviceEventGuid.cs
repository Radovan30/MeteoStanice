using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeteoStanice.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceEventGuid : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeviceEvents_StationId_SequenceNumber",
                table: "DeviceEvents");

            migrationBuilder.AddColumn<Guid>(
                name: "EventGuid",
                table: "DeviceEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE \"DeviceEvents\" SET \"EventGuid\" = \"Id\" WHERE \"EventGuid\" IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "EventGuid",
                table: "DeviceEvents",
                type: "uuid",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEvents_StationId_EventGuid",
                table: "DeviceEvents",
                columns: new[] { "StationId", "EventGuid" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEvents_StationId_SequenceNumber",
                table: "DeviceEvents",
                columns: new[] { "StationId", "SequenceNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DeviceEvents_StationId_EventGuid",
                table: "DeviceEvents");

            migrationBuilder.DropIndex(
                name: "IX_DeviceEvents_StationId_SequenceNumber",
                table: "DeviceEvents");

            migrationBuilder.DropColumn(
                name: "EventGuid",
                table: "DeviceEvents");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceEvents_StationId_SequenceNumber",
                table: "DeviceEvents",
                columns: new[] { "StationId", "SequenceNumber" },
                unique: true);
        }
    }
}
