namespace MeteoStanice
{
    /// <summary>
    /// Defines board-specific constants for LILYGO TTGO T-Display ESP32.
    /// </summary>
    public static class BoardProfile
    {
        // Human-readable board name shown in diagnostics.
        public const string BoardName = "LILYGO TTGO T-Display ESP32 1.14";
        // nanoFramework firmware target used by nanoff for this ESP32 board.
        public const string NanoFrameworkTarget = "ESP32_GenericDisplay_REV0";
        // Default station hardware identity prefix for this board family.
        public const string HardwareIdPrefix = "TTGO-TDISPLAY-ESP32";
        // Common TTGO T-Display I2C data pin for external sensors.
        public const int I2cSdaPin = 21;
        // Common TTGO T-Display I2C clock pin for external sensors.
        public const int I2cSclPin = 22;
        // Default UART transmit pin.
        public const int UartTxPin = 1;
        // Default UART receive pin.
        public const int UartRxPin = 3;
        // TTGO T-Display ST7789 SPI clock pin.
        public const int SpiSckPin = 18;
        // TTGO T-Display unused SPI MISO pin configured for ESP32 SPI routing.
        public const int SpiMisoPin = 12;
        // TTGO T-Display ST7789 SPI MOSI pin.
        public const int SpiMosiPin = 19;
        // TTGO T-Display ST7789 chip select pin.
        public const int DisplayChipSelectPin = 5;
        // TTGO T-Display ST7789 data/command pin.
        public const int DisplayDataCommandPin = 16;
        // TTGO T-Display ST7789 reset pin.
        public const int DisplayResetPin = 23;
        // TTGO T-Display backlight pin.
        public const int DisplayBackLightPin = 4;
        // TTGO T-Display right setup button pin.
        public const int SetupButtonPin = 35;
        // Button hold time required to open setup mode.
        public const int SetupButtonHoldMilliseconds = 2000;
        // TTGO T-Display native portrait screen width.
        public const int DisplayWidth = 135;
        // TTGO T-Display native portrait screen height.
        public const int DisplayHeight = 240;
        // TTGO T-Display ST7789 screen X offset.
        public const int DisplayOffsetX = 52;
        // TTGO T-Display ST7789 screen Y offset.
        public const int DisplayOffsetY = 40;
        // Temporary setup access point name for local configuration after boot.
        public const string ProvisioningAccessPointName = "MeteoStanice-TTGO-Setup";
    }
}
