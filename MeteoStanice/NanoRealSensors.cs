using System;
using System.Device.I2c;
using System.Diagnostics;
using System.Threading;
using nanoFramework.Hardware.Esp32;

namespace MeteoStanice
{
    /// <summary>
    /// Reads real meteo sensors connected to the configured ESP32 I2C bus.
    /// </summary>
    public sealed class NanoRealSensorProvider : INanoFriendlySensors
    {
        private const byte Sht40Address = 0x44;
        private const byte Sps30Address = 0x69;
        private const ushort Sps30StartMeasurementCommand = 0x0010;
        private const ushort Sps30ReadDataReadyCommand = 0x0202;
        private const ushort Sps30ReadMeasuredValuesCommand = 0x0300;
        private const byte Bmp280PrimaryAddress = 0x76;
        private const byte Bmp280SecondaryAddress = 0x77;
        private const byte Bmp280ChipIdRegister = 0xD0;
        private const byte Bmp280ExpectedChipId = 0x58;
        private const byte Bmp280ResetRegister = 0xE0;
        private const byte Bmp280ResetCommand = 0xB6;
        private const byte Bmp280StatusRegister = 0xF3;
        private const byte Bmp280ControlRegister = 0xF4;
        private const byte Bmp280ConfigRegister = 0xF5;
        private const byte Bmp280DataRegister = 0xF7;
        private const byte Bmp280SleepMode = 0x54;
        private const byte Bmp280ForcedMode = 0x55;
        private I2cDevice _sht40;
        private I2cDevice _sps30;
        private I2cDevice _bmp280;
        private bool _busInitialized;
        private bool _sps30Initialized;
        private bool _sps30Unavailable;
        private bool _bmp280Initialized;
        private byte _bmp280Address;
        private byte _bmp280ChipId;
        private ushort _digT1;
        private short _digT2;
        private short _digT3;
        private ushort _digP1;
        private short _digP2;
        private short _digP3;
        private short _digP4;
        private short _digP5;
        private short _digP6;
        private short _digP7;
        private short _digP8;
        private short _digP9;

        /// <summary>
        /// Reads one real SHT40 temperature and humidity sample.
        /// </summary>
        public Sht40ReadResult ReadSht40()
        {
            Sht40ReadResult result = new Sht40ReadResult();
            try
            {
                EnsureBus();
                EnsureSht40();

                byte[] command = new byte[] { 0xFD };
                byte[] data = new byte[6];
                _sht40.Write(command);
                Thread.Sleep(12);
                _sht40.Read(data);

                if (!IsCrcValid(data, 0) || !IsCrcValid(data, 3))
                {
                    result.IsSuccess = false;
                    result.ErrorCode = SensorErrorCode.CrcFailed;
                    result.ErrorMessage = "SHT40 vratil data s chybnym CRC.";
                    return result;
                }

                ushort rawTemperature = ToUInt16(data[0], data[1]);
                ushort rawHumidity = ToUInt16(data[3], data[4]);
                Sht40Sample sample = new Sht40Sample();
                sample.TemperatureCelsius = -45.0 + (175.0 * rawTemperature / 65535.0);
                sample.RelativeHumidityPercent = ClampHumidity(-6.0 + (125.0 * rawHumidity / 65535.0));
                sample.SampledAt = DateTime.UtcNow;
                sample.CrcOk = true;

                result.IsSuccess = true;
                result.Value = sample;
                result.ErrorCode = SensorErrorCode.None;
                result.ErrorMessage = string.Empty;
                return result;
            }
            catch (Exception exception)
            {
                ResetSht40("SHT40 read failed");
                result.IsSuccess = false;
                result.ErrorCode = SensorErrorCode.CommunicationFailed;
                result.ErrorMessage = "SHT40 I2C cteni selhalo: " + exception.Message;
                return result;
            }
        }

        /// <summary>
        /// Reads one real BMP280 pressure sample.
        /// </summary>
        public Bmp280ReadResult ReadBmp280()
        {
            Bmp280ReadResult result = new Bmp280ReadResult();
            try
            {
                EnsureBus();
                EnsureBmp280();

                VerifyBmp280ChipId();
                StartBmp280Measurement();
                byte[] data = ReadBmp280Registers(Bmp280DataRegister, 6);
                ValidateBmp280Data(data);
                int adcPressure = ((data[0] << 12) | (data[1] << 4) | (data[2] >> 4));
                int adcTemperature = ((data[3] << 12) | (data[4] << 4) | (data[5] >> 4));
                int fineTemperature;
                double temperature = CalculateBmp280Temperature(adcTemperature, out fineTemperature);
                double pressure = CalculateBmp280Pressure(adcPressure, fineTemperature);

                Bmp280Sample sample = new Bmp280Sample();
                sample.PressureHpa = pressure;
                sample.AuxiliaryTemperatureCelsius = temperature;
                sample.SampledAt = DateTime.UtcNow;

                result.IsSuccess = true;
                result.Value = sample;
                result.ErrorCode = SensorErrorCode.None;
                result.ErrorMessage = string.Empty;
                return result;
            }
            catch (Exception exception)
            {
                ResetBmp280("BMP280 read failed");
                result.IsSuccess = false;
                result.ErrorCode = SensorErrorCode.CommunicationFailed;
                result.ErrorMessage = "BMP280 I2C cteni selhalo: " + exception.Message;
                return result;
            }
        }

        /// <summary>
        /// Reads one real SPS30 particulate matter sample over I2C.
        /// </summary>
        public Sps30ReadResult ReadSps30()
        {
            Sps30ReadResult result = new Sps30ReadResult();
            try
            {
                if (_sps30Unavailable)
                {
                    result.IsSuccess = false;
                    result.Value = null;
                    result.ErrorCode = SensorErrorCode.NotDetected;
                    result.ErrorMessage = "SPS30 nebyl detekovan na I2C adrese 0x69.";
                    return result;
                }

                EnsureBus();
                EnsureSps30();

                if (!IsSps30DataReady())
                {
                    result.IsSuccess = false;
                    result.Value = null;
                    result.ErrorCode = SensorErrorCode.Timeout;
                    result.ErrorMessage = "SPS30 jeste nema pripraveny vzorek.";
                    return result;
                }

                byte[] data = ReadSps30MeasuredValues();
                Sps30Sample sample = new Sps30Sample();
                sample.MassPm1 = ReadSps30Float(data, 0);
                sample.MassPm25 = ReadSps30Float(data, 1);
                sample.MassPm4 = ReadSps30Float(data, 2);
                sample.MassPm10 = ReadSps30Float(data, 3);
                sample.NumberPm05 = ReadSps30Float(data, 4);
                sample.NumberPm1 = ReadSps30Float(data, 5);
                sample.NumberPm25 = ReadSps30Float(data, 6);
                sample.NumberPm4 = ReadSps30Float(data, 7);
                sample.NumberPm10 = ReadSps30Float(data, 8);
                sample.TypicalParticleSize = ReadSps30Float(data, 9);
                sample.SampledAt = DateTime.UtcNow;

                result.IsSuccess = true;
                result.Value = sample;
                result.ErrorCode = SensorErrorCode.None;
                result.ErrorMessage = string.Empty;
                return result;
            }
            catch (Exception exception)
            {
                result.IsSuccess = false;
                result.Value = null;
                result.ErrorCode = SensorErrorCode.CommunicationFailed;
                result.ErrorMessage = "SPS30 I2C cteni selhalo: " + exception.Message;
                return result;
            }
        }

        private void EnsureBus()
        {
            if (_busInitialized)
            {
                return;
            }

            Configuration.SetPinFunction(BoardProfile.I2cSdaPin, DeviceFunction.I2C1_DATA);
            Configuration.SetPinFunction(BoardProfile.I2cSclPin, DeviceFunction.I2C1_CLOCK);
            _busInitialized = true;
            Debug.WriteLine("I2C ready bus=" + BoardProfile.I2cBusId + ", SDA=" + BoardProfile.I2cSdaPin + ", SCL=" + BoardProfile.I2cSclPin);
        }

        private void EnsureSht40()
        {
            if (_sht40 != null)
            {
                return;
            }

            _sht40 = I2cDevice.Create(new I2cConnectionSettings(BoardProfile.I2cBusId, Sht40Address, I2cBusSpeed.FastMode));
            Debug.WriteLine("SHT40 I2C device created at 0x44.");
        }

        private void EnsureSps30()
        {
            if (_sps30Initialized)
            {
                return;
            }

            try
            {
                _sps30 = I2cDevice.Create(new I2cConnectionSettings(BoardProfile.I2cBusId, Sps30Address, I2cBusSpeed.StandardMode));
                WriteSps30CommandWithWord(Sps30StartMeasurementCommand, 0x0300);
                Thread.Sleep(50);
                _sps30Initialized = true;
                Debug.WriteLine("SPS30 I2C measurement started at 0x69.");
            }
            catch (Exception exception)
            {
                _sps30Unavailable = true;
                Debug.WriteLine("SPS30 init failed: " + exception.Message);
                throw;
            }
        }

        private void EnsureBmp280()
        {
            if (_bmp280Initialized)
            {
                return;
            }

            _bmp280 = CreateBmp280Device(Bmp280PrimaryAddress);
            if (_bmp280 == null)
            {
                _bmp280 = CreateBmp280Device(Bmp280SecondaryAddress);
            }

            if (_bmp280 == null)
            {
                ResetBmp280("BMP280 init failed");
                throw new Exception("BMP280 nebyl detekovan.");
            }

            WriteBmp280Register(Bmp280ResetRegister, Bmp280ResetCommand);
            Thread.Sleep(5);
            ReadBmp280Calibration();
            WriteBmp280Register(Bmp280ConfigRegister, 0x00);
            WriteBmp280Register(Bmp280ControlRegister, Bmp280SleepMode);
            _bmp280Initialized = true;
            Debug.WriteLine("BMP280 initialized at 0x" + _bmp280Address.ToString("X2") + ", chipId=0x" + _bmp280ChipId.ToString("X2") + ".");
        }

        private I2cDevice CreateBmp280Device(byte address)
        {
            try
            {
                I2cDevice device = I2cDevice.Create(new I2cConnectionSettings(BoardProfile.I2cBusId, address, I2cBusSpeed.FastMode));
                byte chipId = ReadRegister(device, Bmp280ChipIdRegister);
                if (chipId == Bmp280ExpectedChipId)
                {
                    _bmp280Address = address;
                    _bmp280ChipId = chipId;
                    Debug.WriteLine("BMP280 detected at 0x" + address.ToString("X2") + ".");
                    return device;
                }

                Debug.WriteLine("Device at 0x" + address.ToString("X2") + " is not BMP280, chipId=0x" + chipId.ToString("X2") + ".");
                device.Dispose();
                return null;
            }
            catch (Exception exception)
            {
                Debug.WriteLine("BMP280 probe failed at 0x" + address.ToString("X2") + ": " + exception.Message);
                return null;
            }
        }

        private void ResetSht40(string reason)
        {
            DisposeDevice(_sht40, reason);
            _sht40 = null;
        }

        private void ResetBmp280(string reason)
        {
            DisposeDevice(_bmp280, reason);
            _bmp280 = null;
            _bmp280Initialized = false;
            _bmp280Address = 0;
            _bmp280ChipId = 0;
        }

        private static void DisposeDevice(I2cDevice device, string reason)
        {
            if (device == null)
            {
                return;
            }

            try
            {
                device.Dispose();
                Debug.WriteLine(reason + ": I2C device disposed for retry.");
            }
            catch (Exception exception)
            {
                Debug.WriteLine(reason + ": I2C dispose failed: " + exception.Message);
            }
        }

        private void StartBmp280Measurement()
        {
            WriteBmp280Register(Bmp280ControlRegister, Bmp280ForcedMode);
            for (int retry = 0; retry < 20; retry++)
            {
                byte status = ReadRegister(_bmp280, Bmp280StatusRegister);
                if ((status & 0x08) == 0)
                {
                    return;
                }

                Thread.Sleep(10);
            }

            Debug.WriteLine("BMP280 measurement timeout, reading last available sample.");
        }

        private void VerifyBmp280ChipId()
        {
            byte chipId = ReadRegister(_bmp280, Bmp280ChipIdRegister);
            if (chipId != Bmp280ExpectedChipId)
            {
                throw new Exception("BMP280 chipId nesedi, read=0x" + chipId.ToString("X2") + ", expected=0x" + Bmp280ExpectedChipId.ToString("X2") + ".");
            }
        }

        private static void ValidateBmp280Data(byte[] data)
        {
            if (data == null || data.Length < 6)
            {
                throw new Exception("BMP280 vratil nekompletni data frame.");
            }

            bool allZero = true;
            bool allHigh = true;
            for (int index = 0; index < data.Length; index++)
            {
                if (data[index] != 0x00)
                {
                    allZero = false;
                }

                if (data[index] != 0xFF)
                {
                    allHigh = false;
                }
            }

            if (allZero || allHigh)
            {
                throw new Exception("BMP280 vratil prazdna data.");
            }
        }

        private void ReadBmp280Calibration()
        {
            byte[] data = ReadBmp280Registers(0x88, 24);
            _digT1 = ToUInt16(data[1], data[0]);
            _digT2 = ToInt16(data[3], data[2]);
            _digT3 = ToInt16(data[5], data[4]);
            _digP1 = ToUInt16(data[7], data[6]);
            _digP2 = ToInt16(data[9], data[8]);
            _digP3 = ToInt16(data[11], data[10]);
            _digP4 = ToInt16(data[13], data[12]);
            _digP5 = ToInt16(data[15], data[14]);
            _digP6 = ToInt16(data[17], data[16]);
            _digP7 = ToInt16(data[19], data[18]);
            _digP8 = ToInt16(data[21], data[20]);
            _digP9 = ToInt16(data[23], data[22]);
        }

        private byte[] ReadBmp280Registers(byte register, int length)
        {
            byte[] writeBuffer = new byte[] { register };
            byte[] readBuffer = new byte[length];
            _bmp280.WriteRead(writeBuffer, readBuffer);
            return readBuffer;
        }

        private void WriteBmp280Register(byte register, byte value)
        {
            _bmp280.Write(new byte[] { register, value });
        }

        private static byte ReadRegister(I2cDevice device, byte register)
        {
            byte[] writeBuffer = new byte[] { register };
            byte[] readBuffer = new byte[1];
            device.WriteRead(writeBuffer, readBuffer);
            return readBuffer[0];
        }

        private bool IsSps30DataReady()
        {
            WriteSps30Command(Sps30ReadDataReadyCommand);
            Thread.Sleep(5);
            byte[] data = new byte[3];
            _sps30.Read(data);
            if (!IsCrcValid(data, 0))
            {
                throw new Exception("SPS30 data-ready CRC selhalo.");
            }

            ushort ready = ToUInt16(data[0], data[1]);
            return ready != 0;
        }

        private byte[] ReadSps30MeasuredValues()
        {
            WriteSps30Command(Sps30ReadMeasuredValuesCommand);
            Thread.Sleep(5);
            byte[] data = new byte[60];
            _sps30.Read(data);
            ValidateSps30MeasuredValues(data);
            return data;
        }

        private void ValidateSps30MeasuredValues(byte[] data)
        {
            for (int index = 0; index < data.Length; index += 3)
            {
                if (!IsCrcValid(data, index))
                {
                    throw new Exception("SPS30 measured values CRC selhalo na word indexu " + (index / 3));
                }
            }
        }

        private double ReadSps30Float(byte[] data, int valueIndex)
        {
            int offset = valueIndex * 6;
            byte[] floatBytes = BitConverter.IsLittleEndian
                ? new byte[] { data[offset + 4], data[offset + 3], data[offset + 1], data[offset] }
                : new byte[] { data[offset], data[offset + 1], data[offset + 3], data[offset + 4] };
            return BitConverter.ToSingle(floatBytes, 0);
        }

        private void WriteSps30Command(ushort command)
        {
            byte[] data = new byte[] { (byte)(command >> 8), (byte)(command & 0xFF) };
            _sps30.Write(data);
        }

        private void WriteSps30CommandWithWord(ushort command, ushort word)
        {
            byte first = (byte)(word >> 8);
            byte second = (byte)(word & 0xFF);
            byte[] data = new byte[]
            {
                (byte)(command >> 8),
                (byte)(command & 0xFF),
                first,
                second,
                CalculateShtCrc(first, second)
            };
            _sps30.Write(data);
        }

        private double CalculateBmp280Temperature(int adcTemperature, out int fineTemperature)
        {
            int var1 = (((adcTemperature >> 3) - ((int)_digT1 << 1)) * _digT2) >> 11;
            int var2 = (((((adcTemperature >> 4) - _digT1) * ((adcTemperature >> 4) - _digT1)) >> 12) * _digT3) >> 14;
            fineTemperature = var1 + var2;
            return ((fineTemperature * 5 + 128) >> 8) / 100.0;
        }

        private double CalculateBmp280Pressure(int adcPressure, int fineTemperature)
        {
            long var1 = fineTemperature - 128000;
            long var2 = var1 * var1 * _digP6;
            var2 = var2 + ((var1 * _digP5) << 17);
            var2 = var2 + (((long)_digP4) << 35);
            var1 = ((var1 * var1 * _digP3) >> 8) + ((var1 * _digP2) << 12);
            var1 = (((((long)1) << 47) + var1) * _digP1) >> 33;
            if (var1 == 0)
            {
                return 0;
            }

            long pressure = 1048576 - adcPressure;
            pressure = (((pressure << 31) - var2) * 3125) / var1;
            var1 = (_digP9 * (pressure >> 13) * (pressure >> 13)) >> 25;
            var2 = (_digP8 * pressure) >> 19;
            pressure = ((pressure + var1 + var2) >> 8) + (((long)_digP7) << 4);
            return pressure / 25600.0;
        }

        private static bool IsCrcValid(byte[] data, int offset)
        {
            return CalculateShtCrc(data[offset], data[offset + 1]) == data[offset + 2];
        }

        private static byte CalculateShtCrc(byte first, byte second)
        {
            byte crc = 0xFF;
            crc = CrcStep(crc, first);
            crc = CrcStep(crc, second);
            return crc;
        }

        private static byte CrcStep(byte crc, byte value)
        {
            crc ^= value;
            for (int bit = 0; bit < 8; bit++)
            {
                if ((crc & 0x80) != 0)
                {
                    crc = (byte)((crc << 1) ^ 0x31);
                }
                else
                {
                    crc = (byte)(crc << 1);
                }
            }

            return crc;
        }

        private static ushort ToUInt16(byte msb, byte lsb)
        {
            return (ushort)((msb << 8) | lsb);
        }

        private static short ToInt16(byte msb, byte lsb)
        {
            return (short)((msb << 8) | lsb);
        }

        private static double ClampHumidity(double humidity)
        {
            if (humidity < 0)
            {
                return 0;
            }

            if (humidity > 100)
            {
                return 100;
            }

            return humidity;
        }
    }
}
