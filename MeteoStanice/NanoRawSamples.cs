using System;
using System.Collections;
using System.Diagnostics;
using System.IO;

namespace MeteoStanice
{
    /// <summary>
    /// Compact raw sensor sample queued for the analysis endpoint.
    /// </summary>
    public sealed class NanoRawSample
    {
        public long SequenceNumber { get; set; }
        public DateTime SampledAt { get; set; }
        public bool HasSht40 { get; set; }
        public double TemperatureCelsius { get; set; }
        public double HumidityPercent { get; set; }
        public SensorErrorCode Sht40Error { get; set; }
        public bool HasBmp280 { get; set; }
        public double PressureHpa { get; set; }
        public double Bmp280TemperatureCelsius { get; set; }
        public SensorErrorCode Bmp280Error { get; set; }
        public bool HasSps30 { get; set; }
        public double Pm1 { get; set; }
        public double Pm25 { get; set; }
        public double Pm4 { get; set; }
        public double Pm10 { get; set; }
        public double NumberPm05 { get; set; }
        public double NumberPm1 { get; set; }
        public double NumberPm25 { get; set; }
        public double NumberPm4 { get; set; }
        public double NumberPm10 { get; set; }
        public double TypicalParticleSize { get; set; }
        public SensorErrorCode Sps30Error { get; set; }
        public bool BatteryActive { get; set; }
        public bool BatteryLow { get; set; }
        public bool HasBatteryVoltage { get; set; }
        public double BatteryVoltage { get; set; }
    }

    /// <summary>
    /// Creates one raw sample on aligned UTC ten-second boundaries.
    /// </summary>
    public sealed class NanoRawSampleBuilder
    {
        private const int RawSamplePeriodSeconds = 10;
        private long _lastSequenceNumber;

        public bool TryBuild(NanoSensorReadSet readSet, NanoPowerState powerState, out NanoRawSample sample)
        {
            if (readSet == null)
            {
                sample = null;
                return false;
            }

            DateTime alignedAt = AlignToPeriod(readSet.SampledAt, RawSamplePeriodSeconds);
            long sequenceNumber = SequenceFromUtc(alignedAt);
            if (sequenceNumber == _lastSequenceNumber)
            {
                sample = null;
                return false;
            }

            _lastSequenceNumber = sequenceNumber;
            sample = new NanoRawSample();
            sample.SequenceNumber = sequenceNumber;
            sample.SampledAt = alignedAt;
            ApplySht40(sample, readSet.Sht40);
            ApplyBmp280(sample, readSet.Bmp280);
            ApplySps30(sample, readSet.Sps30);

            if (powerState != null)
            {
                sample.BatteryActive = powerState.BatteryActive;
                sample.BatteryLow = powerState.BatteryLow;
                sample.HasBatteryVoltage = powerState.HasBatteryVoltage;
                sample.BatteryVoltage = powerState.BatteryVoltage;
            }

            return true;
        }

        private static long SequenceFromUtc(DateTime utcTimestamp)
        {
            return ((long)utcTimestamp.Year * 10000000000L)
                + ((long)utcTimestamp.Month * 100000000L)
                + ((long)utcTimestamp.Day * 1000000L)
                + ((long)utcTimestamp.Hour * 10000L)
                + ((long)utcTimestamp.Minute * 100L)
                + utcTimestamp.Second;
        }

        private static void ApplySht40(NanoRawSample sample, Sht40ReadResult result)
        {
            sample.HasSht40 = result != null && result.IsSuccess && result.Value != null && result.Value.CrcOk;
            sample.Sht40Error = sample.HasSht40
                ? SensorErrorCode.None
                : result == null
                    ? SensorErrorCode.NotDetected
                    : result.ErrorCode == SensorErrorCode.None ? SensorErrorCode.CrcFailed : result.ErrorCode;
            if (sample.HasSht40)
            {
                sample.TemperatureCelsius = result.Value.TemperatureCelsius;
                sample.HumidityPercent = result.Value.RelativeHumidityPercent;
            }
        }

        private static void ApplyBmp280(NanoRawSample sample, Bmp280ReadResult result)
        {
            sample.HasBmp280 = result != null && result.IsSuccess && result.Value != null;
            sample.Bmp280Error = sample.HasBmp280
                ? SensorErrorCode.None
                : result == null ? SensorErrorCode.NotDetected : result.ErrorCode;
            if (sample.HasBmp280)
            {
                sample.PressureHpa = result.Value.PressureHpa;
                sample.Bmp280TemperatureCelsius = result.Value.AuxiliaryTemperatureCelsius;
            }
        }

        private static void ApplySps30(NanoRawSample sample, Sps30ReadResult result)
        {
            sample.HasSps30 = result != null && result.IsSuccess && result.Value != null;
            sample.Sps30Error = sample.HasSps30
                ? SensorErrorCode.None
                : result == null ? SensorErrorCode.NotDetected : result.ErrorCode;
            if (sample.HasSps30)
            {
                sample.Pm1 = result.Value.MassPm1;
                sample.Pm25 = result.Value.MassPm25;
                sample.Pm4 = result.Value.MassPm4;
                sample.Pm10 = result.Value.MassPm10;
                sample.NumberPm05 = result.Value.NumberPm05;
                sample.NumberPm1 = result.Value.NumberPm1;
                sample.NumberPm25 = result.Value.NumberPm25;
                sample.NumberPm4 = result.Value.NumberPm4;
                sample.NumberPm10 = result.Value.NumberPm10;
                sample.TypicalParticleSize = result.Value.TypicalParticleSize;
            }
        }

        private static DateTime AlignToPeriod(DateTime utcTime, int seconds)
        {
            long periodTicks = seconds * TimeSpan.TicksPerSecond;
            return new DateTime(utcTime.Ticks - (utcTime.Ticks % periodTicks), DateTimeKind.Utc);
        }
    }

    /// <summary>
    /// Fixed-size RAM queue of raw samples waiting for upload.
    /// </summary>
    public sealed class NanoRawSampleQueue
    {
        private readonly Queue _items = new Queue();
        private readonly object _syncRoot = new object();
        private readonly int _capacity;
        private int _droppedCount;

        public NanoRawSampleQueue(int capacity)
        {
            _capacity = capacity;
        }

        public int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _items.Count;
                }
            }
        }

        public int Capacity
        {
            get { return _capacity; }
        }

        public int DroppedCount
        {
            get { return _droppedCount; }
        }

        public bool Enqueue(NanoRawSample sample)
        {
            lock (_syncRoot)
            {
                if (sample == null || _items.Count >= _capacity)
                {
                    if (sample != null)
                    {
                        _droppedCount++;
                    }

                    return false;
                }

                _items.Enqueue(sample);
                return true;
            }
        }

        public int CopyOldest(NanoRawSample[] target, int maximum)
        {
            lock (_syncRoot)
            {
                int count = 0;
                IEnumerator enumerator = _items.GetEnumerator();
                while (count < maximum && count < target.Length && enumerator.MoveNext())
                {
                    target[count] = (NanoRawSample)enumerator.Current;
                    count++;
                }

                return count;
            }
        }

        public int RemoveOldest(int count)
        {
            lock (_syncRoot)
            {
                int removed = 0;
                while (removed < count && _items.Count > 0)
                {
                    _items.Dequeue();
                    removed++;
                }

                return removed;
            }
        }
    }

    /// <summary>
    /// Flash-backed fixed-record ring buffer checkpointed before HTTP upload.
    /// Record layout version 2 stores measured values as 32-bit floats (the sensors
    /// do not deliver more precision) so that SPS30 number concentrations fit into
    /// a smaller record than version 1. A file with another version is recreated.
    /// </summary>
    public sealed class NanoRawSamplePersistentQueue
    {
        private const string QueuePath = @"I:\meteo.samples.queue";
        private const int FileMagic = 0x4D535131;
        private const int FileVersion = 2;
        private const int HeaderSize = 24;
        private const int RecordSize = 96;

        private readonly object _syncRoot = new object();
        private readonly int _capacity;
        private int _head;
        private int _count;
        private int _droppedCount;

        public NanoRawSamplePersistentQueue(int capacity)
        {
            _capacity = capacity;
            Initialize();
        }

        public int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _count;
                }
            }
        }

        public int Capacity { get { return _capacity; } }

        public int DroppedCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _droppedCount;
                }
            }
        }

        public int EnqueueBatch(NanoRawSample[] samples, int count)
        {
            if (samples == null || count < 1)
            {
                return 0;
            }

            lock (_syncRoot)
            {
                try
                {
                    using (FileStream stream = new FileStream(QueuePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                    {
                        int stored = 0;
                        while (stored < count && stored < samples.Length)
                        {
                            NanoRawSample sample = samples[stored];
                            if (sample != null)
                            {
                                if (_count >= _capacity)
                                {
                                    _head = (_head + 1) % _capacity;
                                    _count--;
                                    _droppedCount++;
                                }

                                int tail = (_head + _count) % _capacity;
                                stream.Seek(HeaderSize + (tail * RecordSize), SeekOrigin.Begin);
                                byte[] record = WriteSample(sample);
                                stream.Write(record, 0, record.Length);
                                _count++;
                            }

                            stored++;
                        }

                        WriteHeader(stream);
                        return stored;
                    }
                }
                catch (Exception exception)
                {
                    Debug.WriteLine("Raw flash queue zapis selhal: " + exception.Message);
                    return 0;
                }
            }
        }

        public int CopyOldest(NanoRawSample[] target, int maximum)
        {
            if (target == null || maximum < 1)
            {
                return 0;
            }

            lock (_syncRoot)
            {
                try
                {
                    using (FileStream stream = new FileStream(QueuePath, FileMode.Open, FileAccess.Read))
                    {
                        int copied = 0;
                        while (copied < maximum && copied < target.Length && copied < _count)
                        {
                            int index = (_head + copied) % _capacity;
                            stream.Seek(HeaderSize + (index * RecordSize), SeekOrigin.Begin);
                            byte[] record = new byte[RecordSize];
                            if (stream.Read(record, 0, record.Length) != record.Length)
                            {
                                throw new Exception("neuplny zaznam");
                            }

                            target[copied] = ReadSample(record);
                            copied++;
                        }

                        return copied;
                    }
                }
                catch (Exception exception)
                {
                    Debug.WriteLine("Raw flash queue cteni selhalo: " + exception.Message);
                    return 0;
                }
            }
        }

        public int RemoveOldest(int count)
        {
            lock (_syncRoot)
            {
                int removed = count > _count ? _count : count;
                if (removed < 1)
                {
                    return 0;
                }

                int oldHead = _head;
                int oldCount = _count;
                _head = (_head + removed) % _capacity;
                _count -= removed;

                if (!SaveHeader())
                {
                    _head = oldHead;
                    _count = oldCount;
                    return 0;
                }

                return removed;
            }
        }

        private void Initialize()
        {
            lock (_syncRoot)
            {
                try
                {
                    if (!File.Exists(QueuePath))
                    {
                        ResetFile();
                        return;
                    }

                    using (FileStream stream = new FileStream(QueuePath, FileMode.Open, FileAccess.Read))
                    {
                        byte[] header = new byte[HeaderSize];
                        if (stream.Read(header, 0, header.Length) != header.Length)
                        {
                            throw new Exception("neuplna hlavicka");
                        }

                        int magic = BitConverter.ToInt32(header, 0);
                        int version = BitConverter.ToInt32(header, 4);
                        int storedCapacity = BitConverter.ToInt32(header, 8);
                        int head = BitConverter.ToInt32(header, 12);
                        int count = BitConverter.ToInt32(header, 16);
                        int dropped = BitConverter.ToInt32(header, 20);
                        if (magic != FileMagic || version != FileVersion || storedCapacity != _capacity
                            || head < 0 || head >= _capacity || count < 0 || count > _capacity)
                        {
                            throw new Exception("neplatna hlavicka");
                        }

                        _head = head;
                        _count = count;
                        _droppedCount = dropped;
                    }

                    Debug.WriteLine("Raw flash queue obnovena, count=" + _count + "/" + _capacity + ".");
                }
                catch (Exception exception)
                {
                    Debug.WriteLine("Raw flash queue je poskozena, vytvarim novou: " + exception.Message);
                    ResetFile();
                }
            }
        }

        private void ResetFile()
        {
            _head = 0;
            _count = 0;
            _droppedCount = 0;
            using (FileStream stream = new FileStream(QueuePath, FileMode.Create, FileAccess.ReadWrite))
            {
                WriteHeader(stream);
            }
        }

        private bool SaveHeader()
        {
            try
            {
                using (FileStream stream = new FileStream(QueuePath, FileMode.OpenOrCreate, FileAccess.ReadWrite))
                {
                    WriteHeader(stream);
                }

                return true;
            }
            catch (Exception exception)
            {
                Debug.WriteLine("Raw flash queue hlavicku nelze ulozit: " + exception.Message);
                return false;
            }
        }

        private void WriteHeader(FileStream stream)
        {
            byte[] header = new byte[HeaderSize];
            CopyBytes(BitConverter.GetBytes(FileMagic), header, 0);
            CopyBytes(BitConverter.GetBytes(FileVersion), header, 4);
            CopyBytes(BitConverter.GetBytes(_capacity), header, 8);
            CopyBytes(BitConverter.GetBytes(_head), header, 12);
            CopyBytes(BitConverter.GetBytes(_count), header, 16);
            CopyBytes(BitConverter.GetBytes(_droppedCount), header, 20);
            stream.Seek(0, SeekOrigin.Begin);
            stream.Write(header, 0, header.Length);
            stream.Flush();
        }

        private static byte[] WriteSample(NanoRawSample sample)
        {
            int flags = (sample.HasSht40 ? 1 : 0)
                | (sample.HasBmp280 ? 2 : 0)
                | (sample.HasSps30 ? 4 : 0)
                | (sample.BatteryActive ? 8 : 0)
                | (sample.BatteryLow ? 16 : 0)
                | (sample.HasBatteryVoltage ? 32 : 0);

            // Layout v2: 8 B sequence, 8 B ticks, 4 B flags, 16 x 4 B float values, 3 x 4 B error codes = 96 B.
            byte[] record = new byte[RecordSize];
            int offset = 0;
            offset = WriteValue(record, offset, BitConverter.GetBytes(sample.SequenceNumber));
            offset = WriteValue(record, offset, BitConverter.GetBytes(sample.SampledAt.Ticks));
            offset = WriteValue(record, offset, BitConverter.GetBytes(flags));
            offset = WriteFloat(record, offset, sample.TemperatureCelsius);
            offset = WriteFloat(record, offset, sample.HumidityPercent);
            offset = WriteFloat(record, offset, sample.PressureHpa);
            offset = WriteFloat(record, offset, sample.Bmp280TemperatureCelsius);
            offset = WriteFloat(record, offset, sample.Pm1);
            offset = WriteFloat(record, offset, sample.Pm25);
            offset = WriteFloat(record, offset, sample.Pm4);
            offset = WriteFloat(record, offset, sample.Pm10);
            offset = WriteFloat(record, offset, sample.NumberPm05);
            offset = WriteFloat(record, offset, sample.NumberPm1);
            offset = WriteFloat(record, offset, sample.NumberPm25);
            offset = WriteFloat(record, offset, sample.NumberPm4);
            offset = WriteFloat(record, offset, sample.NumberPm10);
            offset = WriteFloat(record, offset, sample.TypicalParticleSize);
            offset = WriteFloat(record, offset, sample.BatteryVoltage);
            offset = WriteFloat(record, offset, 0);
            offset = WriteValue(record, offset, BitConverter.GetBytes((int)sample.Sht40Error));
            offset = WriteValue(record, offset, BitConverter.GetBytes((int)sample.Bmp280Error));
            WriteValue(record, offset, BitConverter.GetBytes((int)sample.Sps30Error));
            return record;
        }

        private static int WriteFloat(byte[] target, int offset, double value)
        {
            return WriteValue(target, offset, BitConverter.GetBytes((float)value));
        }

        private static NanoRawSample ReadSample(byte[] record)
        {
            NanoRawSample sample = new NanoRawSample();
            sample.SequenceNumber = BitConverter.ToInt64(record, 0);
            sample.SampledAt = new DateTime(BitConverter.ToInt64(record, 8), DateTimeKind.Utc);
            int flags = BitConverter.ToInt32(record, 16);
            sample.HasSht40 = (flags & 1) != 0;
            sample.HasBmp280 = (flags & 2) != 0;
            sample.HasSps30 = (flags & 4) != 0;
            sample.BatteryActive = (flags & 8) != 0;
            sample.BatteryLow = (flags & 16) != 0;
            sample.HasBatteryVoltage = (flags & 32) != 0;
            sample.TemperatureCelsius = BitConverter.ToSingle(record, 20);
            sample.HumidityPercent = BitConverter.ToSingle(record, 24);
            sample.PressureHpa = BitConverter.ToSingle(record, 28);
            sample.Bmp280TemperatureCelsius = BitConverter.ToSingle(record, 32);
            sample.Pm1 = BitConverter.ToSingle(record, 36);
            sample.Pm25 = BitConverter.ToSingle(record, 40);
            sample.Pm4 = BitConverter.ToSingle(record, 44);
            sample.Pm10 = BitConverter.ToSingle(record, 48);
            sample.NumberPm05 = BitConverter.ToSingle(record, 52);
            sample.NumberPm1 = BitConverter.ToSingle(record, 56);
            sample.NumberPm25 = BitConverter.ToSingle(record, 60);
            sample.NumberPm4 = BitConverter.ToSingle(record, 64);
            sample.NumberPm10 = BitConverter.ToSingle(record, 68);
            sample.TypicalParticleSize = BitConverter.ToSingle(record, 72);
            sample.BatteryVoltage = BitConverter.ToSingle(record, 76);
            // Offset 80 is reserved for a future value.
            sample.Sht40Error = (SensorErrorCode)BitConverter.ToInt32(record, 84);
            sample.Bmp280Error = (SensorErrorCode)BitConverter.ToInt32(record, 88);
            sample.Sps30Error = (SensorErrorCode)BitConverter.ToInt32(record, 92);
            return sample;
        }

        private static int WriteValue(byte[] target, int offset, byte[] value)
        {
            CopyBytes(value, target, offset);
            return offset + value.Length;
        }

        private static void CopyBytes(byte[] source, byte[] target, int targetOffset)
        {
            for (int index = 0; index < source.Length; index++)
            {
                target[targetOffset + index] = source[index];
            }
        }
    }

    /// <summary>
    /// Uploads raw samples. Normally each fresh 10 s sample is sent directly from RAM; a backlog
    /// (after an outage) is stored in flash and sent oldest first in batches of six. While uploads
    /// are not possible, complete blocks of six samples are checkpointed from RAM to flash.
    /// </summary>
    public sealed class NanoRawSampleUploadService
    {
        private const int CheckpointBatchSize = 6;

        private readonly NanoRawSampleQueue _queue;
        private readonly NanoRawSamplePersistentQueue _persistentQueue;
        private readonly NanoUploadClient _client;
        private int _rejectedCount;

        public NanoRawSampleUploadService(NanoRawSampleQueue queue, NanoRawSamplePersistentQueue persistentQueue, NanoUploadClient client)
        {
            _queue = queue;
            _persistentQueue = persistentQueue;
            _client = client;
        }

        public int PendingCount { get { return _queue.Count + _persistentQueue.Count; } }

        public int RejectedCount { get { return _rejectedCount; } }

        public int TryUploadPending(int maximum)
        {
            if (maximum < 1)
            {
                return 0;
            }

            CheckpointRamQueue(false);

            NanoRawSample[] batch = new NanoRawSample[maximum];
            if (_persistentQueue.Count > 0)
            {
                int persistedCount = _persistentQueue.CopyOldest(batch, maximum);
                if (persistedCount == 0)
                {
                    CheckpointRamQueue(true);
                    return 0;
                }

                _client.SetQueueDiagnostics(_queue.Count + _persistentQueue.Count, _queue.DroppedCount, _persistentQueue.DroppedCount, _rejectedCount);
                if (!_client.UploadSamples(batch, persistedCount))
                {
                    if (_client.LastUploadRejected)
                    {
                        // A permanently rejected batch would block the queue forever.
                        int removed = _persistentQueue.RemoveOldest(persistedCount);
                        _rejectedCount += removed;
                        Debug.WriteLine("Raw samples odmitnuty serverem (HTTP " + _client.LastHttpStatusCode + "), odstraneno z fronty: " + removed + ", celkem odmitnuto=" + _rejectedCount);
                        return 0;
                    }

                    CheckpointRamQueue(true);
                    return 0;
                }

                return _persistentQueue.RemoveOldest(persistedCount);
            }

            // No backlog in flash: send the fresh samples straight from RAM (normally one sample
            // every 10 s). The flash is written only when the upload fails, which keeps flash wear low.
            int ramCount = _queue.CopyOldest(batch, maximum);
            if (ramCount == 0)
            {
                return 0;
            }

            _client.SetQueueDiagnostics(_queue.Count, _queue.DroppedCount, _persistentQueue.DroppedCount, _rejectedCount);
            if (!_client.UploadSamples(batch, ramCount))
            {
                if (_client.LastUploadRejected)
                {
                    int removed = _queue.RemoveOldest(ramCount);
                    _rejectedCount += removed;
                    Debug.WriteLine("Raw samples odmitnuty serverem (HTTP " + _client.LastHttpStatusCode + "), odstraneno z RAM: " + removed + ", celkem odmitnuto=" + _rejectedCount);
                    return 0;
                }

                // Server or network unavailable: keep the samples persistently until it works again.
                CheckpointRamQueue(true);
                return 0;
            }

            return _queue.RemoveOldest(ramCount);
        }

        private void CheckpointRamQueue(bool includeIncompleteBlock)
        {
            int pending = _queue.Count;
            int checkpointCount = includeIncompleteBlock
                ? pending
                : pending - (pending % CheckpointBatchSize);
            if (checkpointCount < 1)
            {
                return;
            }

            NanoRawSample[] samples = new NanoRawSample[checkpointCount];
            int copied = _queue.CopyOldest(samples, checkpointCount);
            int persisted = _persistentQueue.EnqueueBatch(samples, copied);
            if (persisted > 0)
            {
                _queue.RemoveOldest(persisted);
                Debug.WriteLine("Raw samples checkpoint do flash, count=" + persisted + ", flash=" + _persistentQueue.Count + "/" + _persistentQueue.Capacity + ".");
            }
        }
    }
}
