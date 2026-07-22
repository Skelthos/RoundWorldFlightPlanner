using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoundWorldFlightPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddFlightTrackPoints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FlightTrackPoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FlightLegId = table.Column<int>(type: "INTEGER", nullable: false),
                    TimestampUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    AltitudeFt = table.Column<double>(type: "REAL", nullable: false),
                    GroundSpeedKts = table.Column<double>(type: "REAL", nullable: false),
                    VerticalSpeedFpm = table.Column<double>(type: "REAL", nullable: false),
                    HeadingDeg = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FlightTrackPoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FlightTrackPoints_FlightLegs_FlightLegId",
                        column: x => x.FlightLegId,
                        principalTable: "FlightLegs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FlightTrackPoints_FlightLegId",
                table: "FlightTrackPoints",
                column: "FlightLegId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FlightTrackPoints");
        }
    }
}
