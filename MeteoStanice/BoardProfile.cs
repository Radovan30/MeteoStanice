namespace MeteoStanice
{
    /// <summary>
    /// Defines board-specific constants for Seeed Studio XIAO ESP32C3.
    /// </summary>
    public static class BoardProfile
    {
        // Human-readable board name shown in diagnostics.
        public const string BoardName = "Seeed Studio XIAO ESP32C3";
        // nanoFramework firmware target used by nanoff for this ESP32-C3 board.
        public const string NanoFrameworkTarget = "ESP32_C3";
        // Default station hardware identity prefix for this board family.
        public const string HardwareIdPrefix = "XIAO-ESP32C3";
        // XIAO D4, chip GPIO6, recommended I2C data pin.
        public const int I2cSdaPin = 6;
        // XIAO D5, chip GPIO7, recommended I2C clock pin.
        public const int I2cSclPin = 7;
        // XIAO D6, chip GPIO21, default UART transmit pin.
        public const int UartTxPin = 21;
        // XIAO D7, chip GPIO20, default UART receive pin.
        public const int UartRxPin = 20;
        // XIAO D8, chip GPIO8, SPI clock and ESP32-C3 strapping pin.
        public const int SpiSckPin = 8;
        // XIAO D9, chip GPIO9, SPI MISO and ESP32-C3 boot/strapping pin.
        public const int SpiMisoPin = 9;
        // XIAO D10, chip GPIO10, SPI MOSI pin.
        public const int SpiMosiPin = 10;
        // Temporary setup access point name for local configuration after boot.
        public const string ProvisioningAccessPointName = "MeteoStanice-XIAO-Setup";
    }
}
