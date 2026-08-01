using System.Collections;

namespace MeteoStanice
{
    /// <summary>
    /// Provides a bounded FIFO queue for tests; it is not durable across power loss.
    /// </summary>
    /// <typeparam name="T">Queued item type.</typeparam>
    public sealed class InMemoryPersistentQueue<T> : IPersistentQueue<T>
    {
        // ArrayList is available in the nanoFramework core profile.
        private readonly ArrayList _items;
        // Fixed capacity lets tests simulate a full local queue.
        private readonly int _capacity;

        /// <summary>
        /// Creates an in-memory queue with fixed capacity.
        /// </summary>
        public InMemoryPersistentQueue(int capacity)
        {
            // Allocate once at construction to keep runtime allocations predictable.
            _items = new ArrayList();
            _capacity = capacity;
        }

        // Current number of queued items.
        public int Count
        {
            // Count is exposed for diagnostics and queue-full decisions.
            get { return _items.Count; }
        }

        /// <summary>
        /// Adds an item if the queue is not full.
        /// </summary>
        public bool Enqueue(T item)
        {
            // Refuse new items when capacity is reached.
            if (_items.Count >= _capacity)
            {
                return false;
            }

            _items.Add(item);
            return true;
        }

        /// <summary>
        /// Reads the first item without removing it.
        /// </summary>
        public bool TryPeek(out T item)
        {
            // Empty queue is a normal state, not an exception.
            if (_items.Count == 0)
            {
                item = default(T);
                return false;
            }

            item = (T)_items[0];
            return true;
        }

        /// <summary>
        /// Removes and returns the first item.
        /// </summary>
        public bool TryDequeue(out T item)
        {
            // Reuse TryPeek so empty handling stays in one place.
            if (!TryPeek(out item))
            {
                return false;
            }

            _items.RemoveAt(0);
            return true;
        }
    }
}
