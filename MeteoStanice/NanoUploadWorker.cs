using System;
using System.Diagnostics;
using System.Threading;

namespace MeteoStanice
{
    /// <summary>
    /// Uploads queued records and keeps Wi-Fi connected outside the main meteo loop,
    /// so slow network operations never delay the ten-second sampling.
    /// </summary>
    public sealed class NanoUploadWorker
    {
        private const int UploadRetryDelayMilliseconds = 60000;
        // Short pause after a successful upload: a fresh sample (every 10 s) is sent almost
        // immediately and a flash backlog is drained at about 6 samples per second.
        private const int UploadSuccessDelayMilliseconds = 1000;
        private const int IdleDelayMilliseconds = 1000;
        private const int PauseDelayMilliseconds = 100;
        private const int SchedulePollMilliseconds = 200;
        private const int RawSamplesPerBatch = 6;

        private readonly NanoDeviceEventQueue _eventQueue;
        private readonly NanoRawSampleQueue _rawSampleQueue;
        private readonly NanoRawSamplePersistentQueue _persistentRawSampleQueue;
        private readonly NanoDeviceEventUploadService _eventUploadService;
        private readonly NanoRawSampleUploadService _rawSampleUploadService;
        private readonly NanoUploadClient _uploadClient;
        private readonly NanoWifiSupervisor _wifiSupervisor;
        private bool _started;
        private bool _paused;
        private bool _busy;
        private long _nextUploadAttemptTicks;

        public NanoUploadWorker(NanoDeviceEventQueue eventQueue, NanoRawSampleQueue rawSampleQueue, NanoRawSamplePersistentQueue persistentRawSampleQueue, NanoDeviceEventUploadService eventUploadService, NanoRawSampleUploadService rawSampleUploadService, NanoUploadClient uploadClient, NanoWifiSupervisor wifiSupervisor)
        {
            _eventQueue = eventQueue;
            _rawSampleQueue = rawSampleQueue;
            _persistentRawSampleQueue = persistentRawSampleQueue;
            _eventUploadService = eventUploadService;
            _rawSampleUploadService = rawSampleUploadService;
            _uploadClient = uploadClient;
            _wifiSupervisor = wifiSupervisor;
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

        /// <summary>
        /// Pauses the worker and waits until a running upload or Wi-Fi reconnect finishes,
        /// so setup mode can safely take over the Wi-Fi adapter.
        /// </summary>
        public void Pause(int waitMilliseconds)
        {
            _paused = true;
            int waited = 0;
            while (_busy && waited < waitMilliseconds)
            {
                Thread.Sleep(50);
                waited += 50;
            }

            Debug.WriteLine("Upload worker pozastaven" + (_busy ? " (operace stale bezi)." : "."));
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
                // Set busy before reading the pause flag; Pause() then always waits for this iteration.
                _busy = true;
                try
                {
                    if (_paused)
                    {
                        _busy = false;
                        Thread.Sleep(PauseDelayMilliseconds);
                        continue;
                    }

                    if (_wifiSupervisor != null && _wifiSupervisor.ReconnectIfNeeded(_uploadClient.ConsecutiveNetworkFailures))
                    {
                        _uploadClient.ResetNetworkFailures();
                        _nextUploadAttemptTicks = 0;
                    }

                    if (_wifiSupervisor != null && !_wifiSupervisor.IsConnected)
                    {
                        _busy = false;
                        Thread.Sleep(IdleDelayMilliseconds);
                        continue;
                    }

                    if (_eventQueue.Count == 0 && _rawSampleQueue.Count == 0 && _persistentRawSampleQueue.Count == 0)
                    {
                        _busy = false;
                        Thread.Sleep(IdleDelayMilliseconds);
                        continue;
                    }

                    if (!CanAttemptUpload())
                    {
                        _busy = false;
                        Thread.Sleep(SchedulePollMilliseconds);
                        continue;
                    }

                    int uploadedEvents = _eventUploadService.TryUploadPending(1);
                    int uploadedRawSamples = 0;

                    if (uploadedEvents == 0)
                    {
                        uploadedRawSamples = _rawSampleUploadService.TryUploadPending(RawSamplesPerBatch);
                    }

                    if (uploadedEvents > 0 || uploadedRawSamples > 0)
                    {
                        Debug.WriteLine("Upload worker uploaded events=" + uploadedEvents + ", raw samples=" + uploadedRawSamples);
                        ScheduleNextUploadAttempt(true);
                    }
                    else
                    {
                        ScheduleNextUploadAttempt(_uploadClient.LastUploadSucceeded || _uploadClient.LastUploadRejected);
                    }
                }
                catch (Exception exception)
                {
                    Debug.WriteLine("Upload worker chyba: " + exception.Message);
                    ScheduleNextUploadAttempt(false);
                }

                _busy = false;
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
