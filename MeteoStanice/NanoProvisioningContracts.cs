namespace MeteoStanice
{
    /// <summary>
    /// Describes the outcome of the temporary startup setup portal.
    /// </summary>
    public sealed class ProvisioningWindowResult
    {
        // True when a user connected to the setup portal.
        public bool ClientConnected { get; set; }
        // True when a valid configuration was saved before timeout.
        public bool ConfigurationSaved { get; set; }
        // True when the setup window elapsed without saving.
        public bool TimedOut { get; set; }
        // True when setup mode was closed by holding the setup button again.
        public bool CancelledByButton { get; set; }
        // Configuration saved through the setup portal.
        public StationConfiguration Configuration { get; set; }
        // Short diagnostic message written by the portal implementation.
        public string Message { get; set; }
    }

    /// <summary>
    /// Runs the temporary setup access point and web form.
    /// </summary>
    public interface IProvisioningPortal
    {
        /// <summary>
        /// Starts the setup portal for the supplied configuration.
        /// </summary>
        bool Start(StationConfiguration configuration);

        /// <summary>
        /// Waits until the user saves settings or the setup window expires.
        /// </summary>
        ProvisioningWindowResult WaitForResult(int timeoutMilliseconds);

        /// <summary>
        /// Waits until settings are saved or setup mode is closed by button.
        /// </summary>
        ProvisioningWindowResult WaitForResultUntilCancelled(NanoTtgoButtons buttons, int holdMilliseconds);

        /// <summary>
        /// Stops the setup portal and releases network resources.
        /// </summary>
        void Stop();
    }
}
