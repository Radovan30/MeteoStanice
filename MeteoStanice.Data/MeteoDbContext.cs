using MeteoStanice.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MeteoStanice.Data;

/// <summary>
/// Entity Framework Core database context for meteo station backend data.
/// </summary>
public sealed class MeteoDbContext(DbContextOptions<MeteoDbContext> options) : DbContext(options)
{
    /// <summary>
    /// Registered stations.
    /// </summary>
    public DbSet<Station> Stations => Set<Station>();

    /// <summary>
    /// Measurement records.
    /// </summary>
    public DbSet<Measurement> Measurements => Set<Measurement>();

    /// <summary>
    /// Device events.
    /// </summary>
    public DbSet<DeviceEvent> DeviceEvents => Set<DeviceEvent>();

    /// <summary>
    /// Current station settings.
    /// </summary>
    public DbSet<StationSetting> StationSettings => Set<StationSetting>();

    /// <summary>
    /// Sensor channels.
    /// </summary>
    public DbSet<SensorChannel> SensorChannels => Set<SensorChannel>();

    /// <summary>
    /// Quality check definitions.
    /// </summary>
    public DbSet<QualityCheck> QualityChecks => Set<QualityCheck>();

    /// <summary>
    /// Measurement quality flags.
    /// </summary>
    public DbSet<MeasurementQualityFlag> MeasurementQualityFlags => Set<MeasurementQualityFlag>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Station>(entity =>
        {
            entity.HasIndex(station => station.StationGuid).IsUnique();
            entity.HasIndex(station => station.StationCode).IsUnique();
            entity.Property(station => station.StationCode).HasMaxLength(64);
            entity.Property(station => station.Name).HasMaxLength(160);
            entity.Property(station => station.LocationText).HasMaxLength(200);
            entity.Property(station => station.HardwareId).HasMaxLength(120);
            entity.Property(station => station.FirmwareVersion).HasMaxLength(40);
        });

        modelBuilder.Entity<Measurement>(entity =>
        {
            entity.HasIndex(measurement => new { measurement.StationId, measurement.SequenceNumber }).IsUnique();
            entity.HasIndex(measurement => new { measurement.StationId, measurement.MeasuredAt });
            entity.Property(measurement => measurement.PowerSource).HasMaxLength(16);
            entity.Property(measurement => measurement.Status).HasMaxLength(64);
        });

        modelBuilder.Entity<DeviceEvent>(entity =>
        {
            entity.HasIndex(deviceEvent => new { deviceEvent.StationId, deviceEvent.SequenceNumber }).IsUnique();
            entity.Property(deviceEvent => deviceEvent.Severity).HasMaxLength(32);
            entity.Property(deviceEvent => deviceEvent.Category).HasMaxLength(64);
            entity.Property(deviceEvent => deviceEvent.Code).HasMaxLength(80);
            entity.Property(deviceEvent => deviceEvent.Component).HasMaxLength(120);
            entity.Property(deviceEvent => deviceEvent.Message).HasMaxLength(500);
        });

        modelBuilder.Entity<StationSetting>(entity =>
        {
            entity.HasIndex(setting => setting.StationId).IsUnique();
            entity.Property(setting => setting.LocationText).HasMaxLength(200);
        });

        modelBuilder.Entity<SensorChannel>(entity =>
        {
            entity.HasIndex(channel => channel.Code).IsUnique();
            entity.Property(channel => channel.Code).HasMaxLength(64);
            entity.Property(channel => channel.Name).HasMaxLength(120);
            entity.Property(channel => channel.Unit).HasMaxLength(24);
        });

        modelBuilder.Entity<QualityCheck>(entity =>
        {
            entity.HasIndex(check => check.Code).IsUnique();
            entity.Property(check => check.Code).HasMaxLength(64);
            entity.Property(check => check.Name).HasMaxLength(120);
            entity.Property(check => check.Description).HasMaxLength(500);
            entity.Property(check => check.DefaultSeverity).HasMaxLength(32);
        });

        modelBuilder.Entity<MeasurementQualityFlag>(entity =>
        {
            entity.HasIndex(flag => new { flag.MeasurementId, flag.SensorChannelId, flag.QualityCheckId });
            entity.Property(flag => flag.Severity).HasMaxLength(32);
            entity.Property(flag => flag.Message).HasMaxLength(500);
        });
    }
}
