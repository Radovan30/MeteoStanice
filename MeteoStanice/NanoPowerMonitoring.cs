namespace MeteoStanice
{
    /// <summary>
    /// Describes current power source and battery state.
    /// </summary>
    public sealed class NanoPowerState
    {
        // True when main 5V power is available.
        public bool MainsAvailable { get; set; }
        // True when backup battery powers the station.
        public bool BatteryActive { get; set; }
        // Battery voltage estimate.
        public double BatteryVoltage { get; set; }
        // True when battery voltage is below the configured threshold.
        public bool BatteryLow { get; set; }
    }

    /// <summary>
    /// Provides deterministic power-state changes for layered firmware restore.
    /// </summary>
    public sealed class FakeNanoPowerMonitor
    {
        private int _readCount;

        /// <summary>
        /// Reads fake power state; every fifth read simulates battery operation.
        /// </summary>
        public NanoPowerState Read()
        {
            _readCount++;

            bool mainsAvailable = _readCount % 5 != 0;
            NanoPowerState state = new NanoPowerState();
            state.MainsAvailable = mainsAvailable;
            state.BatteryActive = !mainsAvailable;
            state.BatteryVoltage = mainsAvailable ? 4.1 : 3.8;
            state.BatteryLow = state.BatteryVoltage < 3.5;
            return state;
        }
    }

    /// <summary>
    /// Detects power source transitions and reports them as device events.
    /// </summary>
    public sealed class NanoPowerMonitoringService
    {
        private readonly FakeNanoPowerMonitor _powerMonitor;
        private readonly NanoDeviceEventService _eventService;
        private bool _hasLastState;
        private bool _lastMainsAvailable;

        /// <summary>
        /// Creates power monitoring service.
        /// </summary>
        public NanoPowerMonitoringService(FakeNanoPowerMonitor powerMonitor, NanoDeviceEventService eventService)
        {
            _powerMonitor = powerMonitor;
            _eventService = eventService;
        }

        /// <summary>
        /// Reads power state and reports changes against the previous state.
        /// </summary>
        public NanoPowerState ReadAndReport(string stationId)
        {
            NanoPowerState state = _powerMonitor.Read();

            if (!_hasLastState)
            {
                _hasLastState = true;
                _lastMainsAvailable = state.MainsAvailable;
                return state;
            }

            if (_lastMainsAvailable && !state.MainsAvailable)
            {
                _eventService.ClearActive();
                _eventService.Report(stationId, DeviceEventSeverity.Warning, DeviceEventCategory.Power, DeviceEventCode.MainsPowerLost, "Power", "Main power lost, battery active.");
            }
            else if (!_lastMainsAvailable && state.MainsAvailable)
            {
                _eventService.ClearActive();
                _eventService.Report(stationId, DeviceEventSeverity.Information, DeviceEventCategory.Power, DeviceEventCode.MainsPowerRestored, "Power", "Main power restored.");
            }

            _lastMainsAvailable = state.MainsAvailable;
            return state;
        }
    }
}
