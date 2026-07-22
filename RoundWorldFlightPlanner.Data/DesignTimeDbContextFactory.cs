using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace RoundWorldFlightPlanner.Data;

/// <summary>Lets `dotnet ef migrations` scaffold this DbContext without needing the WinForms host to run.</summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<FlightPlannerDbContext>
{
    public FlightPlannerDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<FlightPlannerDbContext>()
            .UseSqlite("Data Source=design_time_only.db")
            .Options;

        return new FlightPlannerDbContext(options);
    }
}
