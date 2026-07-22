using Microsoft.EntityFrameworkCore;

namespace RoundWorldFlightPlanner.Data;

public static class DbContextFactory
{
    public static FlightPlannerDbContext Create(string dbPath)
    {
        var options = new DbContextOptionsBuilder<FlightPlannerDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        var context = new FlightPlannerDbContext(options);
        context.Database.Migrate();
        return context;
    }
}
