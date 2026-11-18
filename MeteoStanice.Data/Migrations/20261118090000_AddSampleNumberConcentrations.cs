using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeteoStanice.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSampleNumberConcentrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "NumberPm05",
                table: "SensorSamples",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NumberPm1",
                table: "SensorSamples",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NumberPm25",
                table: "SensorSamples",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NumberPm4",
                table: "SensorSamples",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NumberPm10",
                table: "SensorSamples",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NumberPm05",
                table: "SensorSamples");

            migrationBuilder.DropColumn(
                name: "NumberPm1",
                table: "SensorSamples");

            migrationBuilder.DropColumn(
                name: "NumberPm25",
                table: "SensorSamples");

            migrationBuilder.DropColumn(
                name: "NumberPm4",
                table: "SensorSamples");

            migrationBuilder.DropColumn(
                name: "NumberPm10",
                table: "SensorSamples");
        }
    }
}
