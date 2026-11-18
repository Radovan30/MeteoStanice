using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeteoStanice.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStationSps30Correction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Sps30CorrectionPercent",
                table: "Stations",
                type: "double precision",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Sps30CorrectionPercent",
                table: "Stations");
        }
    }
}
