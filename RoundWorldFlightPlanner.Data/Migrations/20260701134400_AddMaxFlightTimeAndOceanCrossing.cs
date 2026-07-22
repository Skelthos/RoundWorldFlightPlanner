using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoundWorldFlightPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMaxFlightTimeAndOceanCrossing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "MaxFlightTimeHours",
                table: "Itineraries",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            migrationBuilder.AddColumn<bool>(
                name: "IsOceanCrossing",
                table: "FlightLegs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MaxFlightTimeHours",
                table: "Itineraries");

            migrationBuilder.DropColumn(
                name: "IsOceanCrossing",
                table: "FlightLegs");
        }
    }
}
