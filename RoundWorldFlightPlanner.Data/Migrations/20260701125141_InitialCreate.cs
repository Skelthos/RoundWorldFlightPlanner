using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoundWorldFlightPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Aircraft",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    EmptyWeightKg = table.Column<double>(type: "REAL", nullable: false),
                    EmptyWeightArmM = table.Column<double>(type: "REAL", nullable: false),
                    MaxGrossWeightKg = table.Column<double>(type: "REAL", nullable: false),
                    FuelCapacityKg = table.Column<double>(type: "REAL", nullable: false),
                    FuelBurnKgPerHour = table.Column<double>(type: "REAL", nullable: false),
                    FuelArmM = table.Column<double>(type: "REAL", nullable: false),
                    PilotAndPaxWeightKg = table.Column<double>(type: "REAL", nullable: false),
                    PilotAndPaxArmM = table.Column<double>(type: "REAL", nullable: false),
                    CargoArmM = table.Column<double>(type: "REAL", nullable: false),
                    CruiseSpeedKts = table.Column<double>(type: "REAL", nullable: false),
                    CgForwardLimitM = table.Column<double>(type: "REAL", nullable: false),
                    CgAftLimitM = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Aircraft", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Airports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Icao = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    City = table.Column<string>(type: "TEXT", nullable: false),
                    Country = table.Column<string>(type: "TEXT", nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    ElevationFt = table.Column<int>(type: "INTEGER", nullable: false),
                    SizeClass = table.Column<int>(type: "INTEGER", nullable: false),
                    ContinentCode = table.Column<string>(type: "TEXT", nullable: false),
                    ReserveName = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Airports", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Itineraries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AircraftId = table.Column<int>(type: "INTEGER", nullable: false),
                    HappinessScore = table.Column<int>(type: "INTEGER", nullable: false),
                    CountryGoalPercent = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Itineraries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Itineraries_Aircraft_AircraftId",
                        column: x => x.AircraftId,
                        principalTable: "Aircraft",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FlightLegs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItineraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    SequenceNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    DepartureAirportId = table.Column<int>(type: "INTEGER", nullable: false),
                    ArrivalAirportId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlannedDistanceNm = table.Column<double>(type: "REAL", nullable: false),
                    PlannedCruiseSpeedKts = table.Column<double>(type: "REAL", nullable: false),
                    Phase = table.Column<int>(type: "INTEGER", nullable: false),
                    EngineStartUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TakeoffUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LandingUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ShutDownUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TouchdownVerticalSpeedFpm = table.Column<double>(type: "REAL", nullable: true),
                    LayoverDays = table.Column<int>(type: "INTEGER", nullable: false),
                    AircraftId = table.Column<int>(type: "INTEGER", nullable: true),
                    SimBriefFuelPlanKg = table.Column<double>(type: "REAL", nullable: true),
                    SimBriefAlternateIcao = table.Column<string>(type: "TEXT", nullable: true),
                    SimBriefRouteText = table.Column<string>(type: "TEXT", nullable: true),
                    SimBriefGeneratedUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlightLegs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlightLegs_Aircraft_AircraftId",
                        column: x => x.AircraftId,
                        principalTable: "Aircraft",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FlightLegs_Airports_ArrivalAirportId",
                        column: x => x.ArrivalAirportId,
                        principalTable: "Airports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FlightLegs_Airports_DepartureAirportId",
                        column: x => x.DepartureAirportId,
                        principalTable: "Airports",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_FlightLegs_Itineraries_ItineraryId",
                        column: x => x.ItineraryId,
                        principalTable: "Itineraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CargoItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Description = table.Column<string>(type: "TEXT", nullable: false),
                    WeightKg = table.Column<double>(type: "REAL", nullable: false),
                    IsWifeOutfit = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsShippedHome = table.Column<bool>(type: "INTEGER", nullable: false),
                    AcquiredOnLegId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CargoItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CargoItems_FlightLegs_AcquiredOnLegId",
                        column: x => x.AcquiredOnLegId,
                        principalTable: "FlightLegs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "HappinessEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItineraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    FlightLegId = table.Column<int>(type: "INTEGER", nullable: true),
                    Delta = table.Column<int>(type: "INTEGER", nullable: false),
                    Reason = table.Column<string>(type: "TEXT", nullable: false),
                    OccurredUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HappinessEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HappinessEvents_FlightLegs_FlightLegId",
                        column: x => x.FlightLegId,
                        principalTable: "FlightLegs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HappinessEvents_Itineraries_ItineraryId",
                        column: x => x.ItineraryId,
                        principalTable: "Itineraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Airports_Icao",
                table: "Airports",
                column: "Icao",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CargoItems_AcquiredOnLegId",
                table: "CargoItems",
                column: "AcquiredOnLegId");

            migrationBuilder.CreateIndex(
                name: "IX_FlightLegs_AircraftId",
                table: "FlightLegs",
                column: "AircraftId");

            migrationBuilder.CreateIndex(
                name: "IX_FlightLegs_ArrivalAirportId",
                table: "FlightLegs",
                column: "ArrivalAirportId");

            migrationBuilder.CreateIndex(
                name: "IX_FlightLegs_DepartureAirportId",
                table: "FlightLegs",
                column: "DepartureAirportId");

            migrationBuilder.CreateIndex(
                name: "IX_FlightLegs_ItineraryId",
                table: "FlightLegs",
                column: "ItineraryId");

            migrationBuilder.CreateIndex(
                name: "IX_HappinessEvents_FlightLegId",
                table: "HappinessEvents",
                column: "FlightLegId");

            migrationBuilder.CreateIndex(
                name: "IX_HappinessEvents_ItineraryId",
                table: "HappinessEvents",
                column: "ItineraryId");

            migrationBuilder.CreateIndex(
                name: "IX_Itineraries_AircraftId",
                table: "Itineraries",
                column: "AircraftId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CargoItems");

            migrationBuilder.DropTable(
                name: "HappinessEvents");

            migrationBuilder.DropTable(
                name: "FlightLegs");

            migrationBuilder.DropTable(
                name: "Airports");

            migrationBuilder.DropTable(
                name: "Itineraries");

            migrationBuilder.DropTable(
                name: "Aircraft");
        }
    }
}
