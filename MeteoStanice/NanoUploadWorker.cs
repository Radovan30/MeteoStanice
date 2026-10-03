using System;
using System.Diagnostics;
using System.Threading;

namespace MeteoStanice
{
    /// <summary>
    /// Uploads queued records outside the main meteo loop.
    /// </summary>
    public sealed class NanoUploadWorker
    {
        private const int UploadRetryDelayMilliseconds = 60000;
        private const int UploadSuccessDelayMilliseconds = 5000;
        private const int IdleDelayMilliseconds = 1000;
        private const int PauseDelayMilliseconds = 100;
        private const int SchedulePollMilliseconds = 200;

        private readonly NanoMeasurementQueue _measurementQueue;
        private readonly NanoDeviceEventQueue _eventQueue;
        private readonly NanoMeasurementUploadService _measurementUploadService;
        private readonly NanoDeviceEventUploadService _eventUploadService;
        private readonly NanoUploadClient _uploadClient;
        private bool _started;
        private bool _paused;
        private long _nextUploadAttemptTicks;

        public NanoUploadWorker(NanoMeasurementQueue measurementQueue, NanoDeviceEventQueue eventQueue, NanoMeasurementUploadService measurementUploadService, NanoDeviceEventUploadService eventUploadService, NanoUploadClient uploadClient)
        {
            _measurementQueue = measurementQueue;
            _eventQueue = eventQueue;
            _measurementUploadService = measurementUploadService;
            _eventUploadService = eventUploadService;
            _uploadClient = uploadClient;
        }

        public void Start()
        {
            if (_started)
            {
                return;
            }

            _started = true;
            Thread workerThread = new Thread(Run);
            workerThread.Start();
            Debug.WriteLine("Upload worker spusten mimo hlavni meteo smycku.");
        }

        public void Pause()
        {
            _paused = true;
            Debug.WriteLine("Upload worker pozastaven.");
        }

        public void Resume()
        {
            _paused = false;
            _nextUploadAttemptTicks = 0;
            Debug.WriteLine("Upload worker obnoven.");
        }

        private void Run()
        {
            while (true)
            {
                try
                {
                    if (_paused)
                    {
                        Thread.Sleep(PauseDelayMilliseconds);
                        continue;
                    }

                    if (_measurementQueue.Count == 0 && _eventQueue.Count == 0)
                    {
                        Thread.Sleep(IdleDelayMilliseconds);
                        continue;
                    }

                    if (!CanAttemptUpload())
                    {
                        Thread.Sleep(SchedulePollMilliseconds);
                        continue;
                    }

                    int uploadedMeasurements = _measurementUploadService.TryUploadPending(1);
                    int uploadedEvents = 0;
                    if (uploadedMeasurements == 0)
                    {
                        uploadedEvents = _eventUploadService.TryUploadPending(1);
                    }

                    if (uploadedMeasurements > 0 || uploadedEvents > 0)
                    {
                        Debug.WriteLine("Upload worker uploaded measurements=" + uploadedMeasurements + ", events=" + uploadedEvents);
                        ScheduleNextUploadAttempt(true);
                    }
                    else
                    {
                        ScheduleNextUploadAttempt(_uploadClient.LastUploadSucceeded);
                    }
                }
                catch (Exception exception)
                {
                    Debug.WriteLine("Upload worker chyba: " + exception.Message);
                    ScheduleNextUploadAttempt(false);
                }
            }
        }

        private bool CanAttemptUpload()
        {
            if (_nextUploadAttemptTicks == 0)
            {
                return true;
            }

            if (DateTime.UtcNow.Ticks < _nextUploadAttemptTicks)
            {
                return false;
            }

            _nextUploadAttemptTicks = 0;
            return true;
        }

        private void ScheduleNextUploadAttempt(bool lastUploadSucceeded)
        {
            int delayMilliseconds = lastUploadSucceeded ? UploadSuccessDelayMilliseconds : UploadRetryDelayMilliseconds;
            _nextUploadAttemptTicks = DateTime.UtcNow.Ticks + (delayMilliseconds * TimeSpan.TicksPerMillisecond);
        }
    }
}
