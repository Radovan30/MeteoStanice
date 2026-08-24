using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeteoStanice.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMeasurementRuntimeFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Bmp280ErrorCount",
                table: "Measurements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Bmp280OkCount",
                table: "Measurements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "ExpectedSampleCount",
                table: "Measurements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "HumidityMax",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HumidityMin",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "HumidityStdDev",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NumberPm05",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NumberPm1",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NumberPm10",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NumberPm25",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "NumberPm4",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Pm1",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Pm10Max",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Pm10Min",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Pm10StdDev",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Pm25Max",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Pm25Min",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Pm25StdDev",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Pm4",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PressureMax",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PressureMin",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "PressureStdDev",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReceivedSampleCount",
                table: "Measurements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Sht40ErrorCount",
                table: "Measurements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Sht40OkCount",
                table: "Measurements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Sps30BatterySkippedCount",
                table: "Measurements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Sps30ErrorCount",
                table: "Measurements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Sps30OkCount",
                table: "Measurements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Sps30WaitCount",
                table: "Measurements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "TemperatureMax",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TemperatureMin",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TemperatureStdDev",
                table: "Measurements",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "TypicalParticleSize",
                table: "Measurements",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Bmp280ErrorCount",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Bmp280OkCount",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "ExpectedSampleCount",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "HumidityMax",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "HumidityMin",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "HumidityStdDev",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "NumberPm05",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "NumberPm1",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "NumberPm10",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "NumberPm25",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "NumberPm4",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Pm1",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Pm10Max",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Pm10Min",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Pm10StdDev",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Pm25Max",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Pm25Min",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Pm25StdDev",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Pm4",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "PressureMax",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "PressureMin",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "PressureStdDev",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "ReceivedSampleCount",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Sht40ErrorCount",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Sht40OkCount",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Sps30BatterySkippedCount",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Sps30ErrorCount",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Sps30OkCount",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "Sps30WaitCount",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "TemperatureMax",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "TemperatureMin",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "TemperatureStdDev",
                table: "Measurements");

            migrationBuilder.DropColumn(
                name: "TypicalParticleSize",
                table: "Measurements");
        }
    }
}
