using System;
using System.Device.Adc;
using System.Device.Gpio;
using System.Diagnostics;
using System.Threading;
using nanoFramework.Hardware.Esp32;

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
        // True when BatteryVoltage contains a real board reading.
        public bool HasBatteryVoltage { get; set; }
        // True when battery voltage is below the configured threshold.
        public bool BatteryLow { get; set; }
    }

    /// <summary>
    /// Provides power-state reads for TTGO runtime without simulated transitions.
    /// </summary>
    public sealed class NanoPowerMonitor
    {
        private const int BatteryAdcChannel = 6;
        private const double AdcReferenceVoltage = 3.3;
        private const double BatteryVoltageDividerMultiplier = 2.0;
        private const double BatteryCalibrationMultiplier = 1.0;
        private const double BatteryPresentVoltage = 3.0;
        private const double BatteryLowVoltage = 3.5;
        private const double SourceVoltageThreshold = 4.0;
        private const int AdcWarmupMilliseconds = 10;
        private GpioController _gpioController;
        private AdcController _adcController;
        private AdcChannel _batteryChannel;
        private bool _initialized;
        private bool _initializationFailed;
        private bool _hasLastReportedState;
        private bool _lastReportedBatteryActive;

        /// <summary>
        /// Reads current power state.
        /// </summary>
        public NanoPowerState Read()
        {
            if (!EnsureInitialized())
            {
                return CreateUnknownState();
            }

            try
            {
                EnableBatteryAdc();
                int rawValue = ReadAveragedRawValue();
                double batteryVoltage = CalculateBatteryVoltage(rawValue);
                bool batteryPresent = batteryVoltage >= BatteryPresentVoltage;
                bool batteryActive = batteryPresent && batteryVoltage < SourceVoltageThreshold;

                NanoPowerState state = new NanoPowerState();
                state.MainsAvailable = !batteryActive;
                state.BatteryActive = batteryActive;
                state.BatteryVoltage = batteryVoltage;
                state.HasBatteryVoltage = true;
                state.BatteryLow = batteryPresent && batteryVoltage < BatteryLowVoltage;
                ReportStateIfChanged(rawValue, state);
                return state;
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Power ADC read failed: " + exception.Message);
                return CreateUnknownState();
            }
        }

        private bool EnsureInitialized()
        {
            if (_initialized)
            {
                return true;
            }

            if (_initializationFailed)
            {
                return false;
            }

            try
            {
                _gpioController = new GpioController();
                _gpioController.OpenPin(BoardProfile.BatteryAdcPowerPin, PinMode.Output);
                _gpioController.Write(BoardProfile.BatteryAdcPowerPin, PinValue.High);
                Thread.Sleep(AdcWarmupMilliseconds);
                _adcController = new AdcController();
                _batteryChannel = _adcController.OpenChannel(BatteryAdcChannel);
                _initialized = true;
                Debug.WriteLine("Power ADC ready GPIO" + BoardProfile.BatteryVoltageAdcPin + " channel=" + BatteryAdcChannel + ", min=" + _adcController.MinValue + ", max=" + _adcController.MaxValue);
                return true;
            }
            catch (Exception exception)
            {
                _initializationFailed = true;
                Debug.WriteLine("Power ADC init failed: " + exception.Message);
                return false;
            }
        }

        private void EnableBatteryAdc()
        {
            if (_gpioController != null)
            {
                _gpioController.Write(BoardProfile.BatteryAdcPowerPin, PinValue.High);
                Thread.Sleep(AdcWarmupMilliseconds);
            }
        }

        private int ReadAveragedRawValue()
        {
            int sum = 0;
            const int sampleCount = 4;
            for (int index = 0; index < sampleCount; index++)
            {
                sum += _batteryChannel.ReadValue();
                Thread.Sleep(2);
            }

            return sum / sampleCount;
        }

        private double CalculateBatteryVoltage(int rawValue)
        {
            int minValue = _adcController.MinValue;
            int range = _adcController.MaxValue - minValue;
            if (range <= 0)
            {
                return 0;
            }

            double adcVoltage = ((double)(rawValue - minValue) / range) * AdcReferenceVoltage;
            if (adcVoltage < 0)
            {
                adcVoltage = 0;
            }

            return adcVoltage * BatteryVoltageDividerMultiplier * BatteryCalibrationMultiplier;
        }

        private void ReportStateIfChanged(int rawValue, NanoPowerState state)
        {
            if (_hasLastReportedState && _lastReportedBatteryActive == state.BatteryActive)
            {
                return;
            }

            _hasLastReportedState = true;
            _lastReportedBatteryActive = state.BatteryActive;
            Debug.WriteLine("Power ADC raw=" + rawValue + ", voltage=" + state.BatteryVoltage + ", source=" + (state.BatteryActive ? "BAT" : "SRC"));
        }

        private static NanoPowerState CreateUnknownState()
        {
            NanoPowerState state = new NanoPowerState();
            state.MainsAvailable = true;
            state.BatteryActive = false;
            state.BatteryVoltage = 0;
            state.HasBatteryVoltage = false;
            state.BatteryLow = false;
            return state;
        }
    }

    /// <summary>
    /// Detects power source transitions and reports them as device events.
    /// </summary>
    public sealed class NanoPowerMonitoringService
    {
        private readonly NanoPowerMonitor _powerMonitor;
        private readonly NanoDeviceEventService _eventService;
        private bool _hasLastState;
        private bool _lastMainsAvailable;
        private bool _lastBatteryLow;

        /// <summary>
        /// Creates power monitoring service.
        /// </summary>
        public NanoPowerMonitoringService(NanoPowerMonitor powerMonitor, NanoDeviceEventService eventService)
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
                _lastBatteryLow = state.BatteryLow;
                if (state.BatteryActive)
                {
                    _eventService.Report(stationId, DeviceEventSeverity.Warning, DeviceEventCategory.Power, DeviceEventCode.MainsPowerLost, "Power", "Main power lost, battery active.");
                }

                if (state.BatteryLow)
                {
                    _eventService.Report(stationId, DeviceEventSeverity.Warning, DeviceEventCategory.Power, DeviceEventCode.BatteryLow, "Power", "Battery voltage is low.");
                }

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

            if (!_lastBatteryLow && state.BatteryLow)
            {
                _eventService.ClearActive();
                _eventService.Report(stationId, DeviceEventSeverity.Warning, DeviceEventCategory.Power, DeviceEventCode.BatteryLow, "Power", "Battery voltage is low.");
            }

            _lastMainsAvailable = state.MainsAvailable;
            _lastBatteryLow = state.BatteryLow;
            return state;
        }
    }
}
