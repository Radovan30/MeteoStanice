using System.Diagnostics;

namespace MeteoStanice
{
    /// <summary>
    /// Simulates upload availability without using network libraries.
    /// </summary>
    public sealed class NanoUploadClient
    {
        // True when simulated server accepts records.
        public bool ServerAvailable { get; set; }

        /// <summary>
        /// Creates an upload client with simulated server available.
        /// </summary>
        public NanoUploadClient()
        {
            ServerAvailable = true;
        }

        /// <summary>
        /// Uploads one measurement record to the simulated server.
        /// </summary>
        public bool Upload(NanoMeasurementRecord record)
        {
            if (!ServerAvailable || record == null)
            {
                return false;
            }

            Debug.WriteLine("Uploaded record #" + record.SequenceNumber + " from " + record.StationId);
            return true;
        }

        /// <summary>
        /// Uploads one device event to the simulated server.
        /// </summary>
        public bool UploadEvent(NanoDeviceEvent deviceEvent)
        {
            if (!ServerAvailable || deviceEvent == null)
            {
                return false;
            }

            Debug.WriteLine("Uploaded event #" + deviceEvent.SequenceNumber + " " + deviceEvent.Code.ToString());
            return true;
        }
    }

    /// <summary>
    /// Moves queued records to the upload client after successful acknowledgement.
    /// </summary>
    public sealed class NanoMeasurementUploadService
    {
        private readonly NanoMeasurementQueue _queue;
        private readonly NanoUploadClient _client;

        /// <summary>
        /// Creates upload service for one measurement queue.
        /// </summary>
        public NanoMeasurementUploadService(NanoMeasurementQueue queue, NanoUploadClient client)
        {
            _queue = queue;
            _client = client;
        }

        /// <summary>
        /// Uploads at most one pending record.
        /// </summary>
        public bool TryUploadOne()
        {
            NanoMeasurementRecord record;
            if (!_queue.TryPeek(out record))
            {
                return false;
            }

            if (!_client.Upload(record))
            {
                return false;
            }

            _queue.TryDequeue(out record);
            return true;
        }
    }
}
