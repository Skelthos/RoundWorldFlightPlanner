using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoundWorldFlightPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class FixCascadeDeleteBehavior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CargoItems_FlightLegs_AcquiredOnLegId",
                table: "CargoItems");

            migrationBuilder.DropForeignKey(
                name: "FK_HappinessEvents_FlightLegs_FlightLegId",
                table: "HappinessEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_Itineraries_Aircraft_AircraftId",
                table: "Itineraries");

            migrationBuilder.AddForeignKey(
                name: "FK_CargoItems_FlightLegs_AcquiredOnLegId",
                table: "CargoItems",
                column: "AcquiredOnLegId",
                principalTable: "FlightLegs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_HappinessEvents_FlightLegs_FlightLegId",
                table: "HappinessEvents",
                column: "FlightLegId",
                principalTable: "FlightLegs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Itineraries_Aircraft_AircraftId",
                table: "Itineraries",
                column: "AircraftId",
                principalTable: "Aircraft",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CargoItems_FlightLegs_AcquiredOnLegId",
                table: "CargoItems");

            migrationBuilder.DropForeignKey(
                name: "FK_HappinessEvents_FlightLegs_FlightLegId",
                table: "HappinessEvents");

            migrationBuilder.DropForeignKey(
                name: "FK_Itineraries_Aircraft_AircraftId",
                table: "Itineraries");

            migrationBuilder.AddForeignKey(
                name: "FK_CargoItems_FlightLegs_AcquiredOnLegId",
                table: "CargoItems",
                column: "AcquiredOnLegId",
                principalTable: "FlightLegs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_HappinessEvents_FlightLegs_FlightLegId",
                table: "HappinessEvents",
                column: "FlightLegId",
                principalTable: "FlightLegs",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Itineraries_Aircraft_AircraftId",
                table: "Itineraries",
                column: "AircraftId",
                principalTable: "Aircraft",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
