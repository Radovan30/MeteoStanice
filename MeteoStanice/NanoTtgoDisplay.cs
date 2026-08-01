using System;
using System.Device.Gpio;
using System.Diagnostics;
using System.Drawing;
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
        private bool _initialized;
        private string _lastModeText;
        private string _lastWifiText;
        private string _lastSsidText;
        private string _lastIpText;
        private bool _lastProvisioningActive;
        private bool _modeIndicatorDrawn;

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
                DrawStatus(0, false, false, string.Empty, "0.0.0.0");
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
            DrawStatus(0, provisioningActive, false, string.Empty, "0.0.0.0");
        }

        /// <summary>
        /// Shows provisioning access point details.
        /// </summary>
        public void ShowSetup(string accessPointName, string setupAddress)
        {
            DrawStatus(true, "SETUP", "AP ON", "SSID " + NormalizeDisplayText(accessPointName, 18), "WEB " + NormalizeDisplayText(setupAddress, 18));
        }

        /// <summary>
        /// Shows an early boot loading step.
        /// </summary>
        public void ShowLoading(string step)
        {
            DrawStatus(false, "LOAD", NormalizeDisplayText(step, 18), string.Empty, string.Empty);
        }

        /// <summary>
        /// Shows a short reboot message before switching modes.
        /// </summary>
        public void ShowReboot()
        {
            DrawStatus(true, "REBOOT", "PLEASE WAIT", string.Empty, string.Empty);
        }

        /// <summary>
        /// Shows a compact runtime status block.
        /// </summary>
        public void ShowRuntime(int counter, bool wifiConnected, string wifiSsid, string ipAddress)
        {
            DrawStatus(counter, false, wifiConnected, wifiSsid, ipAddress);
        }

        private void DrawStatus(int counter, bool provisioningActive, bool wifiConnected, string wifiSsid, string ipAddress)
        {
            if (!_initialized)
            {
                return;
            }

            try
            {
                string modeText = provisioningActive ? "SETUP" : "METEO";
                string wifiText = wifiConnected ? "WIFI OK" : "WIFI OFF";
                string ssidText = "SSID " + NormalizeDisplayText(wifiSsid, 18);
                string ipText = "IP " + NormalizeDisplayText(ipAddress, 18);
                DrawStatus(provisioningActive, modeText, wifiText, ssidText, ipText);
            }
            catch (Exception exception)
            {
                Debug.WriteLine("TTGO display kresleni selhalo: " + exception.Message);
            }
        }

        private void DrawStatus(bool provisioningActive, string modeText, string line1, string line2, string line3)
        {
            Color modeColor = provisioningActive ? Color.Orange : Color.LimeGreen;
            Color line1Color = line1 == "WIFI OFF" ? Color.DarkRed : Color.White;

            DrawModeIndicatorIfChanged(provisioningActive);
            DrawTextLineIfChanged(12, 14, 96, 18, modeText, _lastModeText, modeColor, 2);
            DrawTextLineIfChanged(12, 42, 132, 10, line1, _lastWifiText, line1Color, 1);
            DrawTextLineIfChanged(12, 62, 180, 10, line2, _lastSsidText, Color.White, 1);
            DrawTextLineIfChanged(12, 82, 180, 10, line3, _lastIpText, Color.White, 1);

            _lastModeText = modeText;
            _lastWifiText = line1;
            _lastSsidText = line2;
            _lastIpText = line3;
        }

        private void DrawModeIndicatorIfChanged(bool provisioningActive)
        {
            if (_modeIndicatorDrawn && _lastProvisioningActive == provisioningActive)
            {
                return;
            }

            FillLandscapeBlock(LandscapeWidth - 28, 4, 24, 24, Color.Black);
            Color color = provisioningActive ? Color.Orange : Color.LimeGreen;
            DrawLandscapeFilledCircle(LandscapeWidth - 16, 16, 8, color);
            _lastProvisioningActive = provisioningActive;
            _modeIndicatorDrawn = true;
        }

        private void DrawTextLineIfChanged(int x, int y, int width, int height, string text, string previousText, Color color, int scale)
        {
            if (text == previousText)
            {
                return;
            }

            FillLandscapeBlock(x, y, width, height, Color.Black);
            DrawLandscapeText(x, y, text, color, scale);
        }

        private void ResetCachedState()
        {
            FillLandscapeBlock(0, 0, LandscapeWidth, LandscapeHeight, Color.Black);
            _lastModeText = null;
            _lastWifiText = null;
            _lastSsidText = null;
            _lastIpText = null;
            _lastProvisioningActive = false;
            _modeIndicatorDrawn = false;
        }

        private static void DrawLandscapeFilledCircle(int centerX, int centerY, int radius, Color color)
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

            string result = string.Empty;
            int length = value.Length > maxLength ? maxLength : value.Length;
            for (int index = 0; index < length; index++)
            {
                result += NormalizeDisplayCharacter(value[index]);
            }

            return result;
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
                || character == '_')
            {
                return character;
            }

            return '-';
        }

        private static void DrawLandscapeText(int x, int y, string text, Color color, int scale)
        {
            int cursorX = x;

            for (int index = 0; index < text.Length; index++)
            {
                DrawLandscapeCharacter(cursorX, y, text[index], color, scale);
                cursorX += 6 * scale;
            }
        }

        private static void DrawLandscapeCharacter(int x, int y, char character, Color color, int scale)
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

        private static void FillLandscapeBlock(int x, int y, int width, int height, Color color)
        {
            for (int row = 0; row < height; row++)
            {
                for (int column = 0; column < width; column++)
                {
                    WriteLandscapePoint(x + column, y + row, color);
                }
            }
        }

        private static void WriteLandscapePoint(int x, int y, Color color)
        {
            if (x < 0 || x >= LandscapeWidth || y < 0 || y >= LandscapeHeight)
            {
                return;
            }

            int nativeX = y;
            int nativeY = BoardProfile.DisplayHeight - 1 - x;
            DisplayControl.WritePoint((ushort)nativeX, (ushort)nativeY, color);
        }
    }
}
