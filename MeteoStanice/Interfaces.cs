namespace MeteoStanice
{
    /// <summary>
    /// Defines common behavior for a typed sensor driver.
    /// </summary>
    /// <typeparam name="TSample">Sample model returned by the sensor.</typeparam>
    public interface ISensor<TSample>
    {
        /// <summary>
        /// Initializes hardware and verifies basic availability.
        /// </summary>
        SensorInitializationResult Initialize();

        /// <summary>
        /// Reads one sample or returns an expected communication failure.
        /// </summary>
        SensorReadResult<TSample> Read();

        /// <summary>
        /// Returns the last known sensor health.
        /// </summary>
        SensorHealth GetHealth();
    }

    /// <summary>
    /// Defines a persistent FIFO queue used for offline records.
    /// </summary>
    /// <typeparam name="T">Queued item type.</typeparam>
    public interface IPersistentQueue<T>
    {
        // Number of queued items.
        int Count { get; }

        /// <summary>
        /// Adds an item to the queue when capacity allows it.
        /// </summary>
        bool Enqueue(T item);

        /// <summary>
        /// Reads the first item without removing it.
        /// </summary>
        bool TryPeek(out T item);

        /// <summary>
        /// Removes and returns the first item.
        /// </summary>
        bool TryDequeue(out T item);
    }

    /// <summary>
    /// Defines the future server API used by upload services.
    /// </summary>
    public interface IApiClient
    {
        /// <summary>
        /// Uploads one measurement record.
        /// </summary>
        bool UploadMeasurement(MeasurementRecord record);

        /// <summary>
        /// Uploads one device event.
        /// </summary>
        bool UploadEvent(DeviceEvent deviceEvent);

        /// <summary>
        /// Registers or refreshes station identity on the server.
        /// </summary>
        bool RegisterStation(StationIdentity identity);

        /// <summary>
        /// Downloads a newer station configuration when available.
        /// </summary>
        bool TryDownloadConfiguration(StationIdentity identity, int currentConfigurationVersion, out RemoteConfigurationUpdate update);

        /// <summary>
        /// Sends observed Wi-Fi access points for server-side geolocation.
        /// </summary>
        bool TryResolveLocation(StationIdentity identity, WifiAccessPointObservation[] accessPoints, out StationLocation location);
    }

    /// <summary>
    /// Scans nearby Wi-Fi access points for provisioning and location estimation.
    /// </summary>
    public interface IWifiScanner
    {
        /// <summary>
        /// Scans visible Wi-Fi access points.
        /// </summary>
        WifiAccessPointObservation[] Scan();
    }

    /// <summary>
    /// Provides time and synchronization state.
    /// </summary>
    public interface ITimeProvider
    {
        /// <summary>
        /// Gets current station time.
        /// </summary>
        DateTimeState GetNow();

        /// <summary>
        /// Returns whether current time is synchronized with an external source.
        /// </summary>
        bool IsSynchronized();
    }

    /// <summary>
    /// Reads raw power-source state.
    /// </summary>
    public interface IPowerMonitor
    {
        /// <summary>
        /// Reads the current power state.
        /// </summary>
        PowerState Read();
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
        ProvisioningWindowResult WaitForResultUntilCancelled(NanoTtgoButtons buttons);

        /// <summary>
        /// Stops the setup portal and releases network resources.
        /// </summary>
        void Stop();
    }
}
