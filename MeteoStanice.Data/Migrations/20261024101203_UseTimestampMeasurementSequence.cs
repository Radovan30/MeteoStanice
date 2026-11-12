using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MeteoStanice.Data.Migrations
{
    /// <inheritdoc />
    public partial class UseTimestampMeasurementSequence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<long>(
                name: "SequenceNumber",
                table: "Measurements",
                type: "bigint",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "SequenceNumber",
                table: "Measurements",
                type: "integer",
                nullable: false,
                oldClrType: typeof(long),
                oldType: "bigint");
        }
    }
}
