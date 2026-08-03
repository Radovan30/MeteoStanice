using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MeteoStanice.Data;

/// <summary>
/// Design-time factory used by EF Core migration commands.
/// </summary>
public sealed class MeteoDbContextFactory : IDesignTimeDbContextFactory<MeteoDbContext>
{
    /// <inheritdoc />
    public MeteoDbContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<MeteoDbContext> optionsBuilder = new();
        string connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__MeteoDatabase")
            ?? "Host=localhost;Port=5432;Database=meteostanice;Username=postgres;Password=root";

        optionsBuilder.UseNpgsql(connectionString);
        return new MeteoDbContext(optionsBuilder.Options);
    }
}
