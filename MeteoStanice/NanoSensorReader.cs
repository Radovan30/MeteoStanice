using System;

namespace MeteoStanice
{
    /// <summary>
    /// Holds one raw set of readings captured during a runtime sampling cycle.
    /// </summary>
    public sealed class NanoSensorReadSet
    {
        public Sht40ReadResult Sht40 { get; set; }
        public Bmp280ReadResult Bmp280 { get; set; }
        public Sps30ReadResult Sps30 { get; set; }
        public DateTime SampledAt { get; set; }
    }

    /// <summary>
    /// Reads one current set directly from the physical sensors.
    /// </summary>
    public sealed class NanoSensorReader
    {
        private readonly INanoFriendlySensors _sensors;

        public NanoSensorReader(INanoFriendlySensors sensors)
        {
            _sensors = sensors;
        }

        /// <summary>
        /// Reads all sensors. On battery power the SPS30 (fan and laser, the largest consumer)
        /// is stopped and reported as DisabledOnBattery; it is restarted on mains power.
        /// </summary>
        public NanoSensorReadSet ReadCurrentSample(bool batteryActive)
        {
            NanoSensorReadSet readSet = new NanoSensorReadSet();
            readSet.SampledAt = DateTime.UtcNow;
            readSet.Sht40 = _sensors.ReadSht40();
            readSet.Bmp280 = _sensors.ReadBmp280();
            _sensors.SetSps30Enabled(!batteryActive);
            readSet.Sps30 = _sensors.ReadSps30();
            return readSet;
        }
    }
}
