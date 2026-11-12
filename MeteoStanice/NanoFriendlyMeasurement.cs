using System;

namespace MeteoStanice
{
    /// <summary>
    /// Stores one compact measurement record created without generic metadata.
    /// </summary>
    public sealed class NanoMeasurementRecord
    {
        // Station that produced the record.
        public string StationId { get; set; }
        // UTC interval timestamp encoded as yyyyMMddHHmmss for de-duplication.
        public long SequenceNumber { get; set; }
        // Record timestamp.
        public DateTime SampledAt { get; set; }
        // Last valid temperature.
        public double TemperatureCelsius { get; set; }
        // Last valid relative humidity.
        public double RelativeHumidityPercent { get; set; }
        // Last valid atmospheric pressure.
        public double PressureHpa { get; set; }
        // Last valid PM1.0 value.
        public double Pm1 { get; set; }
        // Last valid PM2.5 value.
        public double Pm25 { get; set; }
        // Last valid PM4.0 value.
        public double Pm4 { get; set; }
        // Last valid PM10 value.
        public double Pm10 { get; set; }
        // Last valid number concentration for particles larger than 0.5 um.
        public double NumberPm05 { get; set; }
        // Last valid number concentration for particles larger than 1.0 um.
        public double NumberPm1 { get; set; }
        // Last valid number concentration for particles larger than 2.5 um.
        public double NumberPm25 { get; set; }
        // Last valid number concentration for particles larger than 4.0 um.
        public double NumberPm4 { get; set; }
        // Last valid number concentration for particles larger than 10 um.
        public double NumberPm10 { get; set; }
        // Last valid typical particle size.
        public double TypicalParticleSize { get; set; }
        // Number of successful sensor values included in this record.
        public int ValidValueCount { get; set; }
        // Number of failed sensor reads included in this record.
        public int FailedReadCount { get; set; }
        // Number of samples expected in this interval.
        public int ExpectedSampleCount { get; set; }
        // Number of samples received in this interval.
        public int ReceivedSampleCount { get; set; }
        // Successful SHT40 reads in the interval.
        public int Sht40OkCount { get; set; }
        // Failed SHT40 reads in the interval.
        public int Sht40ErrorCount { get; set; }
        // Successful BMP280 reads in the interval.
        public int Bmp280OkCount { get; set; }
        // Failed BMP280 reads in the interval.
        public int Bmp280ErrorCount { get; set; }
        // Successful SPS30 reads in the interval.
        public int Sps30OkCount { get; set; }
        // SPS30 reads where data was not ready yet.
        public int Sps30WaitCount { get; set; }
        // Failed SPS30 reads in the interval.
        public int Sps30ErrorCount { get; set; }
        // SPS30 reads skipped because station was on battery.
        public int Sps30BatterySkippedCount { get; set; }
        // Minimum temperature observed in the interval.
        public double TemperatureMin { get; set; }
        // Maximum temperature observed in the interval.
        public double TemperatureMax { get; set; }
        // Temperature standard deviation in the interval.
        public double TemperatureStdDev { get; set; }
        // Minimum humidity observed in the interval.
        public double HumidityMin { get; set; }
        // Maximum humidity observed in the interval.
        public double HumidityMax { get; set; }
        // Humidity standard deviation in the interval.
        public double HumidityStdDev { get; set; }
        // Minimum pressure observed in the interval.
        public double PressureMin { get; set; }
        // Maximum pressure observed in the interval.
        public double PressureMax { get; set; }
        // Pressure standard deviation in the interval.
        public double PressureStdDev { get; set; }
        // Minimum PM2.5 observed in the interval.
        public double Pm25Min { get; set; }
        // Maximum PM2.5 observed in the interval.
        public double Pm25Max { get; set; }
        // PM2.5 standard deviation in the interval.
        public double Pm25StdDev { get; set; }
        // Minimum PM10 observed in the interval.
        public double Pm10Min { get; set; }
        // Maximum PM10 observed in the interval.
        public double Pm10Max { get; set; }
        // PM10 standard deviation in the interval.
        public double Pm10StdDev { get; set; }
        // Quality status for this record.
        public MeasurementStatus Status { get; set; }
    }

    /// <summary>
    /// Creates a stable numeric identity from the UTC end of a measurement interval.
    /// </summary>
    internal static class NanoMeasurementSequence
    {
        public static long FromUtc(DateTime utcTimestamp)
        {
            return ((long)utcTimestamp.Year * 10000000000L)
                + ((long)utcTimestamp.Month * 100000000L)
                + ((long)utcTimestamp.Day * 1000000L)
                + ((long)utcTimestamp.Hour * 10000L)
                + ((long)utcTimestamp.Minute * 100L)
                + utcTimestamp.Second;
        }
    }

    /// <summary>
    /// Builds compact measurement records from concrete sensor read results.
    /// </summary>
    public sealed class NanoMeasurementBuilder
    {
        private int _sampleOffsetSeconds;

        /// <summary>
        /// Creates one compact measurement record from latest sensor reads.
        /// </summary>
        public NanoMeasurementRecord Build(string stationId, Sht40ReadResult sht40, Bmp280ReadResult bmp280, Sps30ReadResult sps30)
        {
            _sampleOffsetSeconds++;

            NanoMeasurementRecord record = new NanoMeasurementRecord();
            record.StationId = stationId;
            record.SampledAt = new DateTime(2026, 1, 1, 10, 0, 0).AddSeconds(_sampleOffsetSeconds);
            record.SequenceNumber = NanoMeasurementSequence.FromUtc(record.SampledAt);
            record.Status = MeasurementStatus.Ok;

            ApplySht40(record, sht40);
            ApplyBmp280(record, bmp280);
            ApplySps30(record, sps30);
            return record;
        }

        private void ApplySht40(NanoMeasurementRecord record, Sht40ReadResult result)
        {
            if (result != null && result.IsSuccess && result.Value != null && result.Value.CrcOk)
            {
                record.TemperatureCelsius = result.Value.TemperatureCelsius;
                record.RelativeHumidityPercent = result.Value.RelativeHumidityPercent;
                record.ValidValueCount += 2;
                return;
            }

            record.FailedReadCount++;
            record.Status = MeasurementStatus.Sht40ReadFailed;
        }

        private void ApplyBmp280(NanoMeasurementRecord record, Bmp280ReadResult result)
        {
            if (result != null && result.IsSuccess && result.Value != null)
            {
                record.PressureHpa = result.Value.PressureHpa;
                record.ValidValueCount++;
                return;
            }

            record.FailedReadCount++;
            if (record.Status == MeasurementStatus.Ok)
            {
                record.Status = MeasurementStatus.Bmp280ReadFailed;
            }
        }

        private void ApplySps30(NanoMeasurementRecord record, Sps30ReadResult result)
        {
            if (result != null && result.IsSuccess && result.Value != null)
            {
                record.Pm1 = result.Value.MassPm1;
                record.Pm25 = result.Value.MassPm25;
                record.Pm4 = result.Value.MassPm4;
                record.Pm10 = result.Value.MassPm10;
                record.NumberPm05 = result.Value.NumberPm05;
                record.NumberPm1 = result.Value.NumberPm1;
                record.NumberPm25 = result.Value.NumberPm25;
                record.NumberPm4 = result.Value.NumberPm4;
                record.NumberPm10 = result.Value.NumberPm10;
                record.TypicalParticleSize = result.Value.TypicalParticleSize;
                record.ValidValueCount += 10;
                return;
            }

            record.FailedReadCount++;
            if (record.Status == MeasurementStatus.Ok)
            {
                record.Status = MeasurementStatus.Sps30ReadFailed;
            }
        }
    }
}
