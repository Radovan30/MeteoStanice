using System.Diagnostics;

namespace MeteoStanice
{
    /// <summary>
    /// Provides durable storage for station configuration.
    /// </summary>
    public interface IStationConfigurationStore
    {
        /// <summary>
        /// Attempts to load the last saved station configuration.
        /// </summary>
        bool TryLoad(out StationConfiguration configuration);

        /// <summary>
        /// Saves station configuration after it has been validated.
        /// </summary>
        bool Save(StationConfiguration configuration);
    }

    /// <summary>
    /// Keeps configuration in RAM for desktop and firmware smoke tests.
    /// </summary>
    public sealed class InMemoryStationConfigurationStore : IStationConfigurationStore
    {
        // Last configuration saved in this process.
        private StationConfiguration _configuration;
        // Distinguishes "not saved" from a null reference.
        private bool _hasConfiguration;

        /// <summary>
        /// Attempts to read configuration saved during this process lifetime.
        /// </summary>
        public bool TryLoad(out StationConfiguration configuration)
        {
            // RAM store is useful only for tests inside one firmware run.
            configuration = _configuration;
            return _hasConfiguration;
        }

        /// <summary>
        /// Stores configuration in RAM.
        /// </summary>
        public bool Save(StationConfiguration configuration)
        {
            // Save a reference because this implementation is only for test scenarios.
            _configuration = configuration;
            _hasConfiguration = configuration != null;
            return _hasConfiguration;
        }
    }

    /// <summary>
    /// Placeholder for a flash-backed configuration store.
    /// </summary>
    public sealed class FlashStationConfigurationStore : IStationConfigurationStore
    {
        /// <summary>
        /// Attempts to load configuration from non-volatile memory.
        /// </summary>
        public bool TryLoad(out StationConfiguration configuration)
        {
            // Real implementation will deserialize bytes from flash.
            configuration = null;
            Debug.WriteLine("Flash konfigurace zatim neni napojena na konkretni nanoFramework storage balicek.");
            return false;
        }

        /// <summary>
        /// Persists configuration to non-volatile memory.
        /// </summary>
        public bool Save(StationConfiguration configuration)
        {
            // Real implementation must write atomically to survive power loss.
            Debug.WriteLine("Flash ulozeni konfigurace zatim neni napojene na konkretni nanoFramework storage balicek.");
            return false;
        }
    }
}
