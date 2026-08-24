using System.Collections;

namespace MeteoStanice
{
    /// <summary>
    /// Stores measurement records in a fixed-size RAM queue backed by nanoFramework collections.
    /// </summary>
    public sealed class NanoMeasurementQueue
    {
        private readonly Queue _items;
        private readonly int _capacity;
        private int _droppedCount;

        /// <summary>
        /// Creates a RAM queue with fixed capacity.
        /// </summary>
        public NanoMeasurementQueue(int capacity)
        {
            _items = new Queue();
            _capacity = capacity;
        }

        // Number of queued measurement records.
        public int Count
        {
            get
            {
                return _items.Count;
            }
        }

        // Maximum number of measurement records that fit in the queue.
        public int Capacity
        {
            get
            {
                return _capacity;
            }
        }

        public int DroppedCount
        {
            get
            {
                return _droppedCount;
            }
        }

        /// <summary>
        /// Adds one measurement record when there is free capacity.
        /// </summary>
        public bool Enqueue(NanoMeasurementRecord record)
        {
            if (record == null || _items.Count >= _capacity)
            {
                if (record != null)
                {
                    _droppedCount++;
                }

                return false;
            }

            _items.Enqueue(record);
            return true;
        }

        /// <summary>
        /// Reads the oldest measurement record without removing it.
        /// </summary>
        public bool TryPeek(out NanoMeasurementRecord record)
        {
            if (_items.Count == 0)
            {
                record = null;
                return false;
            }

            record = (NanoMeasurementRecord)_items.Peek();
            return true;
        }

        /// <summary>
        /// Removes and returns the oldest measurement record.
        /// </summary>
        public bool TryDequeue(out NanoMeasurementRecord record)
        {
            if (_items.Count == 0)
            {
                record = null;
                return false;
            }

            record = (NanoMeasurementRecord)_items.Dequeue();
            return true;
        }
    }
}
