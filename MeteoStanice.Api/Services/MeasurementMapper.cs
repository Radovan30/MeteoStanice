using MeteoStanice.Contracts.Measurements;
using MeteoStanice.Contracts.Statuses;
using MeteoStanice.Data.Entities;

namespace MeteoStanice.Api.Services;

/// <summary>
/// Maps a stored ten-minute aggregate to its DTO. Raw values stay untouched; the SPS30
/// percentage correction of the station is added as separate Corrected* fields.
/// </summary>
public static class MeasurementMapper
{
    public static MeasurementDto ToDto(Measurement measurement, Station station, int qualityFlagCount)
        => new()
        {
            Id = measurement.Id,
            StationGuid = station.StationGuid,
            StationCode = station.StationCode,
            StationName = station.Name,
            SequenceNumber = measurement.SequenceNumber,
            MeasuredAt = measurement.MeasuredAt,
            TemperatureCelsius = measurement.TemperatureCelsius,
            HumidityPercent = measurement.HumidityPercent,
            PressureHpa = measurement.PressureHpa,
            Pm1 = measurement.Pm1,
            Pm25 = measurement.Pm25,
            Pm4 = measurement.Pm4,
            Pm10 = measurement.Pm10,
            Sps30CorrectionPercent = station.Sps30CorrectionPercent,
            CorrectedPm1 = ApplySps30Correction(measurement.Pm1, station.Sps30CorrectionPercent),
            CorrectedPm25 = ApplySps30Correction(measurement.Pm25, station.Sps30CorrectionPercent),
            CorrectedPm4 = ApplySps30Correction(measurement.Pm4, station.Sps30CorrectionPercent),
            CorrectedPm10 = ApplySps30Correction(measurement.Pm10, station.Sps30CorrectionPercent),
            NumberPm05 = measurement.NumberPm05,
            NumberPm1 = measurement.NumberPm1,
            NumberPm25 = measurement.NumberPm25,
            NumberPm4 = measurement.NumberPm4,
            NumberPm10 = measurement.NumberPm10,
            TypicalParticleSize = measurement.TypicalParticleSize,
            BatteryVoltage = measurement.BatteryVoltage,
            PowerSource = measurement.PowerSource,
            ExpectedSampleCount = measurement.ExpectedSampleCount,
            ReceivedSampleCount = measurement.ReceivedSampleCount,
            Sht40OkCount = measurement.Sht40OkCount,
            Sht40ErrorCount = measurement.Sht40ErrorCount,
            Bmp280OkCount = measurement.Bmp280OkCount,
            Bmp280ErrorCount = measurement.Bmp280ErrorCount,
            Sps30OkCount = measurement.Sps30OkCount,
            Sps30WaitCount = measurement.Sps30WaitCount,
            Sps30ErrorCount = measurement.Sps30ErrorCount,
            Sps30BatterySkippedCount = measurement.Sps30BatterySkippedCount,
            TemperatureMin = measurement.TemperatureMin,
            TemperatureMax = measurement.TemperatureMax,
            TemperatureStdDev = measurement.TemperatureStdDev,
            HumidityMin = measurement.HumidityMin,
            HumidityMax = measurement.HumidityMax,
            HumidityStdDev = measurement.HumidityStdDev,
            PressureMin = measurement.PressureMin,
            PressureMax = measurement.PressureMax,
            PressureStdDev = measurement.PressureStdDev,
            Pm25Min = measurement.Pm25Min,
            Pm25Max = measurement.Pm25Max,
            Pm25StdDev = measurement.Pm25StdDev,
            Pm10Min = measurement.Pm10Min,
            Pm10Max = measurement.Pm10Max,
            Pm10StdDev = measurement.Pm10StdDev,
            Status = FirmwareStatusCatalog.NormalizeMeasurementStatus(measurement.Status),
            QualityFlagCount = qualityFlagCount
        };

    public static double? ApplySps30Correction(double? value, double correctionPercent)
        => value * (1 + correctionPercent / 100.0);
}
