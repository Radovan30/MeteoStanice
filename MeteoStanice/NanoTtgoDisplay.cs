using System;
using System.Device.Gpio;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using nanoFramework.Hardware.Esp32;
using nanoFramework.UI;
using nanoFramework.UI.GraphicDrivers;

namespace MeteoStanice
{
    /// <summary>
    /// Provides a lightweight visual status output for the TTGO T-Display ST7789 screen.
    /// </summary>
    public sealed class NanoTtgoDisplay
    {
        private const int LandscapeWidth = 240;
        private const int LandscapeHeight = 135;
        private GpioController _gpioController;
        private nanoFramework.UI.Bitmap _surface;
        private bool _initialized;
        private string _lastModeText;
        private string _lastDateText;
        private string _lastTimeText;
        private string _lastWifiText;
        private string _lastSsidText;
        private string _lastIpText;
        private string _lastLocationText;
        private string _lastPowerText;
        private string _lastSensorStatusText;
        private bool _lastProvisioningActive;
        private bool _lastWifiConnected;
        private bool _modeIndicatorDrawn;
        private bool _wifiIndicatorDrawn;

        /// <summary>
        /// Initializes SPI routing and the ST7789 display driver.
        /// </summary>
        public bool Initialize()
        {
            try
            {
                Configuration.SetPinFunction(BoardProfile.SpiMisoPin, DeviceFunction.SPI1_MISO);
                Configuration.SetPinFunction(BoardProfile.SpiMosiPin, DeviceFunction.SPI1_MOSI);
                Configuration.SetPinFunction(BoardProfile.SpiSckPin, DeviceFunction.SPI1_CLOCK);
                TurnBacklightOn();

                SpiConfiguration spiConfiguration = new SpiConfiguration(
                    1,
                    BoardProfile.DisplayChipSelectPin,
                    BoardProfile.DisplayDataCommandPin,
                    BoardProfile.DisplayResetPin,
                    -1);

                ScreenConfiguration screenConfiguration = new ScreenConfiguration(
                    (ushort)BoardProfile.DisplayOffsetX,
                    (ushort)BoardProfile.DisplayOffsetY,
                    (ushort)BoardProfile.DisplayWidth,
                    (ushort)BoardProfile.DisplayHeight,
                    St7789.GraphicDriver);

                Debug.WriteLine("TTGO display DisplayControl.Initialize start.");
                DisplayControl.Initialize(spiConfiguration, screenConfiguration, 1024);
                Debug.WriteLine("TTGO display DisplayControl.Initialize hotovo.");
                _surface = null;
                DisplayControl.Clear();
                _initialized = true;
                ResetCachedState();
                Debug.WriteLine("TTGO display inicializovan.");
                DrawStartupPattern();
                return true;
            }
            catch (Exception exception)
            {
                _initialized = false;
                Debug.WriteLine("TTGO display inicializace selhala: " + exception.Message);
                return false;
            }
        }

        private void TurnBacklightOn()
        {
            try
            {
                _gpioController = new GpioController();
                _gpioController.OpenPin(BoardProfile.DisplayBackLightPin, PinMode.Output);
                _gpioController.Write(BoardProfile.DisplayBackLightPin, PinValue.High);
                Debug.WriteLine("TTGO display backlight GPIO" + BoardProfile.DisplayBackLightPin + "=High.");
            }
            catch (Exception exception)
            {
                Debug.WriteLine("TTGO display backlight nelze zapnout: " + exception.Message);
            }
        }

        private void DrawStartupPattern()
        {
            try
            {
                DisplayControl.Clear();
                ResetCachedState();
                DrawStatus(0, false, false, string.Empty, "0.0.0.0", string.Empty);
                Debug.WriteLine("TTGO display startup pattern vykreslen.");
            }
            catch (Exception exception)
            {
                Debug.WriteLine("TTGO display startup pattern selhal: " + exception.Message);
            }
        }

        /// <summary>
        /// Shows the first boot status block.
        /// </summary>
        public void ShowBoot(bool provisioningActive)
        {
            DrawStatus(0, provisioningActive, false, string.Empty, "0.0.0.0", string.Empty);
        }

        /// <summary>
        /// Shows provisioning access point details.
        /// </summary>
        public void ShowSetup(string accessPointName, string setupAddress)
        {
            DrawStatus(true, false, "SETUP", "AP ON", "SSID " + NormalizeDisplayText(accessPointName, 18), "WEB " + NormalizeDisplayText(setupAddress, 18));
        }

        /// <summary>
        /// Shows that the station is connecting to the configured Wi-Fi.
        /// </summary>
        public void ShowWifiConnecting(string wifiSsid)
        {
            DrawStatus(false, false, "METEO", WifiSsidLine(wifiSsid, false, string.Empty), "IP 0.0.0.0", "WIFI START");
        }

        /// <summary>
        /// Shows an early boot loading step.
        /// </summary>
        public void ShowLoading(string step)
        {
            DrawStatus(false, false, "BOOT", NormalizeDisplayText(step, 18), string.Empty, string.Empty);
        }

        /// <summary>
        /// Shows a short reboot message before switching modes.
        /// </summary>
        public void ShowReboot()
        {
            DrawStatus(true, false, "REBOOT", "PLEASE WAIT", string.Empty, string.Empty);
        }

        /// <summary>
        /// Shows a compact runtime status block.
        /// </summary>
        public void ShowRuntime(int counter, bool wifiConnected, string wifiSsid, string ipAddress)
        {
            ShowRuntime(counter, wifiConnected, wifiSsid, ipAddress, string.Empty);
        }

        /// <summary>
        /// Shows a compact runtime status block with an optional Wi-Fi diagnostic line.
        /// </summary>
        public void ShowRuntime(int counter, bool wifiConnected, string wifiSsid, string ipAddress, string wifiMessage)
        {
            ShowRuntime(counter, wifiConnected, wifiSsid, ipAddress, wifiMessage, string.Empty);
        }

        /// <summary>
        /// Shows a compact runtime status block with current station time.
        /// </summary>
        public void ShowRuntime(int counter, bool wifiConnected, string wifiSsid, string ipAddress, string wifiMessage, string timeText)
        {
            ShowRuntime(counter, wifiConnected, wifiSsid, ipAddress, wifiMessage, string.Empty, timeText);
        }

        /// <summary>
        /// Shows a compact runtime status block with current station date and time.
        /// </summary>
        public void ShowRuntime(int counter, bool wifiConnected, string wifiSsid, string ipAddress, string wifiMessage, string dateText, string timeText)
        {
            ShowRuntime(counter, wifiConnected, wifiSsid, ipAddress, wifiMessage, dateText, timeText, string.Empty);
        }

        /// <summary>
        /// Shows a compact runtime status block with current station date, time and location.
        /// </summary>
        public void ShowRuntime(int counter, bool wifiConnected, string wifiSsid, string ipAddress, string wifiMessage, string dateText, string timeText, string locationText)
        {
            ShowRuntime(counter, wifiConnected, wifiSsid, ipAddress, wifiMessage, dateText, timeText, locationText, false, 0, false);
        }

        /// <summary>
        /// Shows a compact runtime status block with current station date, time, location and power source.
        /// </summary>
        public void ShowRuntime(int counter, bool wifiConnected, string wifiSsid, string ipAddress, string wifiMessage, string dateText, string timeText, string locationText, bool batteryActive)
        {
            ShowRuntime(counter, wifiConnected, wifiSsid, ipAddress, wifiMessage, dateText, timeText, locationText, batteryActive, 0, false);
        }

        /// <summary>
        /// Shows a compact runtime status block with current station date, time, location, power source and battery voltage.
        /// </summary>
        public void ShowRuntime(int counter, bool wifiConnected, string wifiSsid, string ipAddress, string wifiMessage, string dateText, string timeText, string locationText, bool batteryActive, double batteryVoltage, bool hasBatteryVoltage)
        {
            DrawStatus(counter, false, wifiConnected, wifiSsid, ipAddress, wifiMessage, dateText, timeText, locationText, batteryActive, batteryVoltage, hasBatteryVoltage);
        }

        /// <summary>
        /// Shows the latest sensor health summary as colored indicators.
        /// </summary>
        public void ShowSensorStatus(NanoSensorReadSet readSet)
        {
            if (!_initialized || readSet == null)
            {
                return;
            }

            try
            {
                string statusText = SensorStatusText(readSet);
                if (statusText == _lastSensorStatusText)
                {
                    return;
                }

                FillLandscapeBlock(12, 120, 210, 12, Color.Black);
                DrawSensorIndicator(12, 121, "SHT", SensorColor(readSet.Sht40));
                DrawSensorIndicator(70, 121, "BMP", SensorColor(readSet.Bmp280));
                DrawSensorIndicator(128, 121, "SPS", SensorColor(readSet.Sps30));
                FlushLandscapeBlock(12, 120, 210, 12);
                _lastSensorStatusText = statusText;
            }
            catch (Exception exception)
            {
                Debug.WriteLine("TTGO display sensor status selhal: " + exception.Message);
            }
        }

        private void DrawStatus(int counter, bool provisioningActive, bool wifiConnected, string wifiSsid, string ipAddress, string wifiMessage)
        {
            DrawStatus(counter, provisioningActive, wifiConnected, wifiSsid, ipAddress, wifiMessage, string.Empty, string.Empty, string.Empty, false, 0, false);
        }

        private void DrawStatus(int counter, bool provisioningActive, bool wifiConnected, string wifiSsid, string ipAddress, string wifiMessage, string timeText)
        {
            DrawStatus(counter, provisioningActive, wifiConnected, wifiSsid, ipAddress, wifiMessage, string.Empty, timeText, string.Empty, false, 0, false);
        }

        private void DrawStatus(int counter, bool provisioningActive, bool wifiConnected, string wifiSsid, string ipAddress, string wifiMessage, string dateText, string timeText)
        {
            DrawStatus(counter, provisioningActive, wifiConnected, wifiSsid, ipAddress, wifiMessage, dateText, timeText, string.Empty, false, 0, false);
        }

        private void DrawStatus(int counter, bool provisioningActive, bool wifiConnected, string wifiSsid, string ipAddress, string wifiMessage, string dateText, string timeText, string locationText, bool batteryActive, double batteryVoltage, bool hasBatteryVoltage)
        {
            if (!_initialized)
            {
                return;
            }

            try
            {
                string modeText = provisioningActive ? "SETUP" : "METEO";
                string ssidText = WifiSsidLine(wifiSsid, wifiConnected, wifiMessage);
                string ipText = "IP " + NormalizeDisplayText(ipAddress, 18);
                DrawStatus(provisioningActive, wifiConnected, modeText, ssidText, ipText, NormalizeDisplayText(wifiMessage, 18), dateText, timeText, locationText, batteryActive, batteryVoltage, hasBatteryVoltage);
            }
            catch (Exception exception)
            {
                Debug.WriteLine("TTGO display kresleni selhalo: " + exception.Message);
            }
        }

        private void DrawStatus(bool provisioningActive, bool wifiConnected, string modeText, string line1, string line2, string line3)
        {
            DrawStatus(provisioningActive, wifiConnected, modeText, line1, line2, line3, string.Empty, string.Empty, string.Empty, false, 0, false);
        }

        private void DrawStatus(bool provisioningActive, bool wifiConnected, string modeText, string line1, string line2, string line3, string timeText)
        {
            DrawStatus(provisioningActive, wifiConnected, modeText, line1, line2, line3, string.Empty, timeText, string.Empty, false, 0, false);
        }

        private void DrawStatus(bool provisioningActive, bool wifiConnected, string modeText, string line1, string line2, string line3, string dateText, string timeText)
        {
            DrawStatus(provisioningActive, wifiConnected, modeText, line1, line2, line3, dateText, timeText, string.Empty, false, 0, false);
        }

        private void DrawStatus(bool provisioningActive, bool wifiConnected, string modeText, string line1, string line2, string line3, string dateText, string timeText, string locationText, bool batteryActive, double batteryVoltage, bool hasBatteryVoltage)
        {
            Color modeColor = provisioningActive ? Color.Orange : Color.LimeGreen;
            Color line1Color = line1 == "WIFI OFF" ? Color.DarkRed : Color.White;
            string normalizedDateText = NormalizeOptionalDisplayText(dateText, 10);
            string normalizedTimeText = NormalizeOptionalDisplayText(timeText, 5);
            string normalizedLocationText = LocationLine(locationText);
            string powerText = PowerLine(batteryActive);

            DrawModeIndicatorIfChanged(provisioningActive);
            DrawWifiIndicatorIfChanged(wifiConnected);
            DrawTextLineIfChanged(12, 6, 76, 18, modeText, _lastModeText, modeColor, 2);
            DrawTextLineIfChanged(CenteredX(94, 114, normalizedDateText, 1), 7, 70, 8, normalizedDateText, _lastDateText, Color.White, 1);
            DrawTextLineIfChanged(CenteredX(94, 114, normalizedTimeText, 1), 18, 42, 8, normalizedTimeText, _lastTimeText, Color.White, 1);
            DrawTextLineIfChanged(12, 42, 180, 10, line1, _lastWifiText, Color.White, 1);
            DrawTextLineIfChanged(12, 62, 180, 10, line2, _lastSsidText, Color.White, 1);
            DrawTextLineIfChanged(12, 82, 180, 10, line3, _lastIpText, Color.White, 1);
            DrawTextLineIfChanged(12, 102, 150, 10, normalizedLocationText, _lastLocationText, Color.White, 1);
            DrawTextLineIfChanged(204, 102, 24, 10, powerText, _lastPowerText, PowerColor(batteryActive), 1);

            _lastModeText = modeText;
            _lastDateText = normalizedDateText;
            _lastTimeText = normalizedTimeText;
            _lastWifiText = line1;
            _lastSsidText = line2;
            _lastIpText = line3;
            _lastLocationText = normalizedLocationText;
            _lastPowerText = powerText;
        }

        private void DrawModeIndicatorIfChanged(bool provisioningActive)
        {
            if (_modeIndicatorDrawn && _lastProvisioningActive == provisioningActive)
            {
                return;
            }

            Color color = provisioningActive ? Color.Orange : Color.LimeGreen;
            FillLandscapeBlock(LandscapeWidth - 26, 6, 16, 16, Color.Black);
            FillLandscapeBlock(LandscapeWidth - 23, 9, 10, 10, color);
            FlushLandscapeBlock(LandscapeWidth - 26, 6, 16, 16);
            _lastProvisioningActive = provisioningActive;
            _modeIndicatorDrawn = true;
        }

        private void DrawWifiIndicatorIfChanged(bool wifiConnected)
        {
            if (_wifiIndicatorDrawn && _lastWifiConnected == wifiConnected)
            {
                return;
            }

            Color color = wifiConnected ? Color.LimeGreen : Color.DarkRed;
            FillLandscapeBlock(LandscapeWidth - 26, 38, 16, 16, Color.Black);
            FillLandscapeBlock(LandscapeWidth - 23, 41, 10, 10, color);
            FlushLandscapeBlock(LandscapeWidth - 26, 38, 16, 16);
            _lastWifiConnected = wifiConnected;
            _wifiIndicatorDrawn = true;
        }

        private void DrawTextLineIfChanged(int x, int y, int width, int height, string text, string previousText, Color color, int scale)
        {
            if (text == previousText)
            {
                return;
            }

            int clearWidth = MaxTextWidth(text, previousText, scale) + (scale * 2);
            if (clearWidth > width)
            {
                clearWidth = width;
            }

            FillLandscapeBlock(x, y, clearWidth, height, Color.Black);
            DrawLandscapeText(x, y, text, color, scale);
            FlushLandscapeBlock(x, y, clearWidth, height);
        }

        private void ResetCachedState()
        {
            _lastModeText = null;
            _lastDateText = null;
            _lastTimeText = null;
            _lastWifiText = null;
            _lastSsidText = null;
            _lastIpText = null;
            _lastLocationText = null;
            _lastPowerText = null;
            _lastSensorStatusText = null;
            _lastProvisioningActive = false;
            _lastWifiConnected = false;
            _modeIndicatorDrawn = false;
            _wifiIndicatorDrawn = false;
        }

        private void DrawSensorIndicator(int x, int y, string label, Color color)
        {
            DrawLandscapeText(x, y, label, Color.White, 1);
            FillLandscapeBlock(x + 24, y + 1, 8, 8, color);
        }

        private static string SensorStatusText(NanoSensorReadSet readSet)
        {
            return SensorState(readSet.Sht40) + "|" + SensorState(readSet.Bmp280) + "|" + SensorState(readSet.Sps30);
        }

        private static string SensorState(Sht40ReadResult result)
        {
            return result != null && result.IsSuccess ? "OK" : result == null ? "--" : "ERR";
        }

        private static string SensorState(Bmp280ReadResult result)
        {
            return result != null && result.IsSuccess ? "OK" : result == null ? "--" : "ERR";
        }

        private static string SensorState(Sps30ReadResult result)
        {
            if (result == null)
            {
                return "--";
            }

            if (result.IsSuccess)
            {
                return "OK";
            }

            return result.ErrorCode == SensorErrorCode.DisabledOnBattery ? "BAT" : result.ErrorCode == SensorErrorCode.Timeout ? "WAIT" : "ERR";
        }

        private static Color SensorColor(Sht40ReadResult result)
        {
            return result == null ? Color.Orange : result.IsSuccess ? Color.LimeGreen : Color.DarkRed;
        }

        private static Color SensorColor(Bmp280ReadResult result)
        {
            return result == null ? Color.Orange : result.IsSuccess ? Color.LimeGreen : Color.DarkRed;
        }

        private static Color SensorColor(Sps30ReadResult result)
        {
            if (result == null)
            {
                return Color.Orange;
            }

            if (result.IsSuccess)
            {
                return Color.LimeGreen;
            }

            return result.ErrorCode == SensorErrorCode.DisabledOnBattery || result.ErrorCode == SensorErrorCode.Timeout ? Color.Orange : Color.DarkRed;
        }

        private void DrawLandscapeFilledCircle(int centerX, int centerY, int radius, Color color)
        {
            int radiusSquared = radius * radius;

            for (int y = -radius; y <= radius; y++)
            {
                for (int x = -radius; x <= radius; x++)
                {
                    if ((x * x) + (y * y) <= radiusSquared)
                    {
                        WriteLandscapePoint(centerX + x, centerY + y, color);
                    }
                }
            }
        }

        private static string NormalizeDisplayText(string value, int maxLength)
        {
            if (value == null || value.Length == 0)
            {
                return "-";
            }

            StringBuilder result = new StringBuilder();
            int length = value.Length > maxLength ? maxLength : value.Length;
            for (int index = 0; index < length; index++)
            {
                result.Append(NormalizeDisplayCharacter(value[index]));
            }

            return result.ToString();
        }

        private static string NormalizeOptionalDisplayText(string value, int maxLength)
        {
            if (value == null || value.Length == 0)
            {
                return string.Empty;
            }

            return NormalizeDisplayText(value, maxLength);
        }

        private static int MaxTextWidth(string currentText, string previousText, int scale)
        {
            int currentWidth = TextWidth(currentText, scale);
            int previousWidth = TextWidth(previousText, scale);
            return currentWidth > previousWidth ? currentWidth : previousWidth;
        }

        private static int TextWidth(string text, int scale)
        {
            if (text == null || text.Length == 0)
            {
                return 0;
            }

            return text.Length * 6 * scale;
        }

        private static int CenteredX(int areaX, int areaWidth, string text, int scale)
        {
            int textWidth = TextWidth(text, scale);
            if (textWidth >= areaWidth)
            {
                return areaX;
            }

            return areaX + ((areaWidth - textWidth) / 2);
        }

        private static string WifiSsidLine(string wifiSsid, bool wifiConnected, string wifiMessage)
        {
            return "SSID " + NormalizeDisplayText(wifiSsid, 20) + " | " + WifiShortState(wifiConnected, wifiMessage);
        }

        private static string LocationLine(string locationText)
        {
            if (locationText == null || locationText.Length == 0)
            {
                return "LOC -";
            }

            return "LOC " + NormalizeDisplayText(locationText, 20);
        }

        private static string PowerLine(bool batteryActive)
        {
            return batteryActive ? "BAT" : "SRC";
        }

        private static Color PowerColor(bool batteryActive)
        {
            return batteryActive ? Color.Orange : Color.LimeGreen;
        }

        private static string WifiShortState(bool wifiConnected, string wifiMessage)
        {
            if (wifiConnected)
            {
                return "OK";
            }

            if (wifiMessage == null || wifiMessage.Length == 0)
            {
                return "--";
            }

            if (wifiMessage.IndexOf("WIFI START") >= 0
                || wifiMessage.IndexOf("ADAPTER") >= 0
                || wifiMessage.IndexOf("JMENO") >= 0
                || wifiMessage.IndexOf("IP CONFIG") >= 0
                || wifiMessage.IndexOf("DISCONNECT") >= 0
                || wifiMessage.IndexOf("POKUS") >= 0
                || wifiMessage.IndexOf("PRIPOJUJI") >= 0)
            {
                return "--";
            }

            if (wifiMessage.IndexOf("HESLO") >= 0)
            {
                return "HESLO";
            }

            if (wifiMessage.IndexOf("SIT") >= 0)
            {
                return "SIT";
            }

            if (wifiMessage.IndexOf("DHCP") >= 0)
            {
                return "DHCP";
            }

            if (wifiMessage.IndexOf("CAS") >= 0)
            {
                return "CAS";
            }

            if (wifiMessage.IndexOf("AUTH") >= 0)
            {
                return "AUTH";
            }

            return "ERR";
        }

        private static char NormalizeDisplayCharacter(char character)
        {
            if (character >= 'a' && character <= 'z')
            {
                return (char)(character - 32);
            }

            if ((character >= 'A' && character <= 'Z')
                || (character >= '0' && character <= '9')
                || character == '.'
                || character == '-'
                || character == '_'
                || character == '|'
                || character == ':'
                || character == ' ')
            {
                return character;
            }

            return '-';
        }

        private void DrawLandscapeText(int x, int y, string text, Color color, int scale)
        {
            int cursorX = x;

            for (int index = 0; index < text.Length; index++)
            {
                DrawLandscapeCharacter(cursorX, y, text[index], color, scale);
                cursorX += 6 * scale;
            }
        }

        private void DrawLandscapeCharacter(int x, int y, char character, Color color, int scale)
        {
            for (int row = 0; row < 7; row++)
            {
                byte bits = GetGlyphRow(character, row);

                for (int column = 0; column < 5; column++)
                {
                    if ((bits & (1 << (4 - column))) != 0)
                    {
                        FillLandscapeBlock(x + (column * scale), y + (row * scale), scale, scale, color);
                    }
                }
            }
        }

        private static byte GetGlyphRow(char character, int row)
        {
            switch (character)
            {
                case 'A':
                    return GetRow(row, 0x0E, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11);
                case 'B':
                    return GetRow(row, 0x1E, 0x11, 0x11, 0x1E, 0x11, 0x11, 0x1E);
                case 'C':
                    return GetRow(row, 0x0E, 0x11, 0x10, 0x10, 0x10, 0x11, 0x0E);
                case 'D':
                    return GetRow(row, 0x1E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x1E);
                case 'E':
                    return GetRow(row, 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x1F);
                case 'F':
                    return GetRow(row, 0x1F, 0x10, 0x10, 0x1E, 0x10, 0x10, 0x10);
                case 'G':
                    return GetRow(row, 0x0E, 0x11, 0x10, 0x17, 0x11, 0x11, 0x0F);
                case 'H':
                    return GetRow(row, 0x11, 0x11, 0x11, 0x1F, 0x11, 0x11, 0x11);
                case 'I':
                    return GetRow(row, 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x1F);
                case 'J':
                    return GetRow(row, 0x01, 0x01, 0x01, 0x01, 0x11, 0x11, 0x0E);
                case 'K':
                    return GetRow(row, 0x11, 0x12, 0x14, 0x18, 0x14, 0x12, 0x11);
                case 'L':
                    return GetRow(row, 0x10, 0x10, 0x10, 0x10, 0x10, 0x10, 0x1F);
                case 'M':
                    return GetRow(row, 0x11, 0x1B, 0x15, 0x15, 0x11, 0x11, 0x11);
                case 'N':
                    return GetRow(row, 0x11, 0x19, 0x15, 0x13, 0x11, 0x11, 0x11);
                case 'O':
                    return GetRow(row, 0x0E, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E);
                case 'P':
                    return GetRow(row, 0x1E, 0x11, 0x11, 0x1E, 0x10, 0x10, 0x10);
                case 'Q':
                    return GetRow(row, 0x0E, 0x11, 0x11, 0x11, 0x15, 0x12, 0x0D);
                case 'R':
                    return GetRow(row, 0x1E, 0x11, 0x11, 0x1E, 0x14, 0x12, 0x11);
                case 'S':
                    return GetRow(row, 0x0F, 0x10, 0x10, 0x0E, 0x01, 0x01, 0x1E);
                case 'T':
                    return GetRow(row, 0x1F, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04);
                case 'U':
                    return GetRow(row, 0x11, 0x11, 0x11, 0x11, 0x11, 0x11, 0x0E);
                case 'V':
                    return GetRow(row, 0x11, 0x11, 0x11, 0x11, 0x0A, 0x0A, 0x04);
                case 'W':
                    return GetRow(row, 0x11, 0x11, 0x11, 0x15, 0x15, 0x15, 0x0A);
                case 'X':
                    return GetRow(row, 0x11, 0x11, 0x0A, 0x04, 0x0A, 0x11, 0x11);
                case 'Y':
                    return GetRow(row, 0x11, 0x11, 0x0A, 0x04, 0x04, 0x04, 0x04);
                case 'Z':
                    return GetRow(row, 0x1F, 0x01, 0x02, 0x04, 0x08, 0x10, 0x1F);
                case '0':
                    return GetRow(row, 0x0E, 0x11, 0x13, 0x15, 0x19, 0x11, 0x0E);
                case '1':
                    return GetRow(row, 0x04, 0x0C, 0x04, 0x04, 0x04, 0x04, 0x0E);
                case '2':
                    return GetRow(row, 0x0E, 0x11, 0x01, 0x02, 0x04, 0x08, 0x1F);
                case '3':
                    return GetRow(row, 0x1E, 0x01, 0x01, 0x0E, 0x01, 0x01, 0x1E);
                case '4':
                    return GetRow(row, 0x02, 0x06, 0x0A, 0x12, 0x1F, 0x02, 0x02);
                case '5':
                    return GetRow(row, 0x1F, 0x10, 0x10, 0x1E, 0x01, 0x01, 0x1E);
                case '6':
                    return GetRow(row, 0x06, 0x08, 0x10, 0x1E, 0x11, 0x11, 0x0E);
                case '7':
                    return GetRow(row, 0x1F, 0x01, 0x02, 0x04, 0x08, 0x08, 0x08);
                case '8':
                    return GetRow(row, 0x0E, 0x11, 0x11, 0x0E, 0x11, 0x11, 0x0E);
                case '9':
                    return GetRow(row, 0x0E, 0x11, 0x11, 0x0F, 0x01, 0x02, 0x0C);
                case '.':
                    return GetRow(row, 0x00, 0x00, 0x00, 0x00, 0x00, 0x0C, 0x0C);
                case '-':
                    return GetRow(row, 0x00, 0x00, 0x00, 0x1F, 0x00, 0x00, 0x00);
                case '_':
                    return GetRow(row, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x1F);
                case '|':
                    return GetRow(row, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04, 0x04);
                case ':':
                    return GetRow(row, 0x00, 0x0C, 0x0C, 0x00, 0x0C, 0x0C, 0x00);
                default:
                    return 0x00;
            }
        }

        private static byte GetRow(int row, byte row0, byte row1, byte row2, byte row3, byte row4, byte row5, byte row6)
        {
            switch (row)
            {
                case 0:
                    return row0;
                case 1:
                    return row1;
                case 2:
                    return row2;
                case 3:
                    return row3;
                case 4:
                    return row4;
                case 5:
                    return row5;
                default:
                    return row6;
            }
        }

        private void FillLandscapeBlock(int x, int y, int width, int height, Color color)
        {
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    WriteLandscapePoint(x + column, y + row, color);
                }
            }
        }

        private void WriteLandscapePoint(int x, int y, Color color)
        {
            if (x < 0 || x >= LandscapeWidth || y < 0 || y >= LandscapeHeight)
            {
                return;
            }

            int nativeX = y;
            int nativeY = BoardProfile.DisplayHeight - 1 - x;
            if (_surface != null)
            {
                _surface.SetPixel(nativeX, nativeY, color);
                return;
            }

            DisplayControl.WritePoint((ushort)nativeX, (ushort)nativeY, color);
        }

        private void FlushLandscapeBlock(int x, int y, int width, int height)
        {
            if (_surface == null || width <= 0 || height <= 0)
            {
                return;
            }

            if (x < 0)
            {
                width += x;
                x = 0;
            }

            if (y < 0)
            {
                height += y;
                y = 0;
            }

            if (x + width > LandscapeWidth)
            {
                width = LandscapeWidth - x;
            }

            if (y + height > LandscapeHeight)
            {
                height = LandscapeHeight - y;
            }

            if (width <= 0 || height <= 0)
            {
                return;
            }

            int nativeX = y;
            int nativeY = BoardProfile.DisplayHeight - x - width;
            _surface.Flush(nativeX, nativeY, height, width);
        }
    }
}
