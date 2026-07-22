using Microsoft.EntityFrameworkCore;
using RoundWorldFlightPlanner.Core.Models;

namespace RoundWorldFlightPlanner.Data;

public class FlightPlannerDbContext(DbContextOptions<FlightPlannerDbContext> options) : DbContext(options)
{
    public DbSet<Airport> Airports => Set<Airport>();
    public DbSet<Aircraft> Aircraft => Set<Aircraft>();
    public DbSet<Itinerary> Itineraries => Set<Itinerary>();
    public DbSet<FlightLeg> FlightLegs => Set<FlightLeg>();
    public DbSet<CargoItem> CargoItems => Set<CargoItem>();
    public DbSet<HappinessEvent> HappinessEvents => Set<HappinessEvent>();
    public DbSet<AppSettings> AppSettings => Set<AppSettings>();
    public DbSet<FlightTrackPoint> FlightTrackPoints => Set<FlightTrackPoint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Airport>()
            .HasIndex(a => a.Icao)
            .IsUnique();

        modelBuilder.Entity<FlightLeg>()
            .HasOne(l => l.DepartureAirport)
            .WithMany()
            .HasForeignKey(l => l.DepartureAirportId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FlightLeg>()
            .HasOne(l => l.ArrivalAirport)
            .WithMany()
            .HasForeignKey(l => l.ArrivalAirportId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FlightLeg>()
            .HasOne(l => l.Itinerary)
            .WithMany(i => i.Legs)
            .HasForeignKey(l => l.ItineraryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Cascade: CargoItems and HappinessEvents have no independent existence outside a leg/itinerary,
        // so deleting a trip must be able to remove them without hitting a foreign key violation.
        modelBuilder.Entity<CargoItem>()
            .HasOne(c => c.AcquiredOnLeg)
            .WithMany()
            .HasForeignKey(c => c.AcquiredOnLegId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FlightLeg>()
            .HasOne(l => l.Aircraft)
            .WithMany()
            .HasForeignKey(l => l.AircraftId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Itinerary>()
            .HasOne(i => i.Aircraft)
            .WithMany()
            .HasForeignKey(i => i.AircraftId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<HappinessEvent>()
            .HasOne(h => h.Itinerary)
            .WithMany(i => i.HappinessEvents)
            .HasForeignKey(h => h.ItineraryId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<HappinessEvent>()
            .HasOne(h => h.FlightLeg)
            .WithMany()
            .HasForeignKey(h => h.FlightLegId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FlightTrackPoint>()
            .HasOne(t => t.FlightLeg)
            .WithMany(l => l.TrackPoints)
            .HasForeignKey(t => t.FlightLegId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
