using MeteoStanice.Api.Hubs;
using MeteoStanice.Data;
using MeteoStanice.Quality;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

string connectionString = builder.Configuration.GetConnectionString("MeteoDatabase")
    ?? throw new InvalidOperationException("Connection string 'MeteoDatabase' is not configured.");

builder.Services.AddDbContext<MeteoDbContext>(options =>
    options.UseNpgsql(
        connectionString,
        npgsqlOptions => npgsqlOptions.MigrationsAssembly(typeof(MeteoDbContext).Assembly.FullName)));
builder.Services.AddScoped<MeasurementQualityService>();
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddSignalR();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Dashboard", policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetIsOriginAllowed(_ => true);
    });
});

var app = builder.Build();

using (IServiceScope scope = app.Services.CreateScope())
{
    ILogger<Program> logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    MeteoDbContext dbContext = scope.ServiceProvider.GetRequiredService<MeteoDbContext>();
    string[] pendingMigrations = dbContext.Database.GetPendingMigrations().ToArray();
    string[] appliedMigrations = dbContext.Database.GetAppliedMigrations().ToArray();
    logger.LogInformation(
        "Database migrations before startup migrate: applied={AppliedCount}, pending={PendingCount}, pendingList={PendingMigrations}",
        appliedMigrations.Length,
        pendingMigrations.Length,
        string.Join(", ", pendingMigrations));

    dbContext.Database.Migrate();

    appliedMigrations = dbContext.Database.GetAppliedMigrations().ToArray();
    logger.LogInformation("Database migrations after startup migrate: applied={AppliedCount}", appliedMigrations.Length);
}

app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.RoutePrefix = "swagger";
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "MeteoStanice API v1");
});

app.UseCors("Dashboard");
app.UseAuthorization();
app.MapGet("/", () => Results.Redirect("/swagger")).ExcludeFromDescription();
app.MapGet("/api", () => Results.Ok(new
{
    status = "ok",
    service = "MeteoStanice.Api",
    timeUtc = DateTimeOffset.UtcNow
}));
app.MapControllers();
app.MapHub<MeteoHub>("/hubs/meteo");

app.Run();
