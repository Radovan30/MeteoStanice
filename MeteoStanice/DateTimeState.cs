using System;

namespace MeteoStanice
{
    /// <summary>
    /// Wraps DateTime so time providers can evolve without changing callers.
    /// </summary>
    public sealed class DateTimeState
    {
        // Provided date and time value.
        public DateTime Value { get; set; }
    }
}
