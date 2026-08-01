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
        // Monotonic station-local sequence number.
        public int SequenceNumber { get; set; }
        // Record timestamp.
        public DateTime SampledAt { get; set; }
        // Last valid temperature.
        public double TemperatureCelsius { get; set; }
        // Last valid relative humidity.
        public double RelativeHumidityPercent { get; set; }
        // Last valid atmospheric pressure.
        public double PressureHpa { get; set; }
        // Last valid PM2.5 value.
        public double Pm25 { get; set; }
        // Last valid PM10 value.
        public double Pm10 { get; set; }
        // Number of successful sensor values included in this record.
        public int ValidValueCount { get; set; }
        // Number of failed sensor reads included in this record.
        public int FailedReadCount { get; set; }
        // Quality status for this record.
        public MeasurementStatus Status { get; set; }
    }

    /// <summary>
    /// Builds compact measurement records from concrete sensor read results.
    /// </summary>
    public sealed class NanoMeasurementBuilder
    {
        private int _sequenceNumber;

        /// <summary>
        /// Creates one compact measurement record from latest sensor reads.
        /// </summary>
        public NanoMeasurementRecord Build(string stationId, Sht40ReadResult sht40, Bmp280ReadResult bmp280, Sps30ReadResult sps30)
        {
            _sequenceNumber++;

            NanoMeasurementRecord record = new NanoMeasurementRecord();
            record.StationId = stationId;
            record.SequenceNumber = _sequenceNumber;
            record.SampledAt = new DateTime(2026, 1, 1, 10, 0, 0).AddSeconds(_sequenceNumber);
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
                record.Pm25 = result.Value.MassPm25;
                record.Pm10 = result.Value.MassPm10;
                record.ValidValueCount += 2;
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
