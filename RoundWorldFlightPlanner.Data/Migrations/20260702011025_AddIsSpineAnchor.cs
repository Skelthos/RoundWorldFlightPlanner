using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RoundWorldFlightPlanner.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddIsSpineAnchor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSpineAnchor",
                table: "FlightLegs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            // Backfill for itineraries created before this column existed: every spine ICAO is
            // marked "used" before RouteAutoFillService/LegSplittingService ever run, so no
            // auto-generated leg could ever arrive at one of these airports - this exclusively and
            // correctly identifies the original narrative legs, not just new ones going forward.
            migrationBuilder.Sql(@"
                UPDATE FlightLegs
                SET IsSpineAnchor = 1
                WHERE ArrivalAirportId IN (
                    SELECT Id FROM Airports WHERE Icao IN (
                        'KVGT','KDEN','KSFO','KOLM','PAFA','RJTT','VHHH','WSSS','YPDN','YBCS',
                        'YSSY','YMML','YPAD','YPPH','NZRO','NFFN','VABB','VNKT','OMDB','HECA',
                        'HKJK','FBSK','FACT','DNMM','GMMN','LEMD','EDDH','EDDM','EFRO','EINN',
                        'BGSF','CYYT','KLEB','TJSJ','SKBO','SPZO','SCEL','NZWD','SAEZ','MPTO',
                        'MMCU','KJAN'
                    )
                );
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSpineAnchor",
                table: "FlightLegs");
        }
    }
}
