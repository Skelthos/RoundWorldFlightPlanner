using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoundWorldFlightPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRunwayPerformanceFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsPavedRunway",
                table: "Airports",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LongestRunwayFt",
                table: "Airports",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<double>(
                name: "LandingDistanceFtAtMaxGrossWeight",
                table: "Aircraft",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<double>(
                name: "TakeoffDistanceFtAtMaxGrossWeight",
                table: "Aircraft",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsPavedRunway",
                table: "Airports");

            migrationBuilder.DropColumn(
                name: "LongestRunwayFt",
                table: "Airports");

            migrationBuilder.DropColumn(
                name: "LandingDistanceFtAtMaxGrossWeight",
                table: "Aircraft");

            migrationBuilder.DropColumn(
                name: "TakeoffDistanceFtAtMaxGrossWeight",
                table: "Aircraft");
        }
    }
}
