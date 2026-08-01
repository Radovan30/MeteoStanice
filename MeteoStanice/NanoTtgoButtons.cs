using System;
using System.Device.Gpio;
using System.Diagnostics;
using System.Threading;

namespace MeteoStanice
{
    /// <summary>
    /// Reads TTGO T-Display buttons and detects setup button clicks.
    /// </summary>
    public sealed class NanoTtgoButtons
    {
        private GpioController _gpioController;
        private bool _initialized;
        private bool _requestConsumed;
        private bool _ignoreUntilReleased;
        private long _blockedUntilTicks;

        /// <summary>
        /// Initializes the setup button input.
        /// </summary>
        public bool Initialize()
        {
            try
            {
                _gpioController = new GpioController();
                _gpioController.OpenPin(BoardProfile.SetupButtonPin, PinMode.Input);
                _initialized = true;
                Debug.WriteLine("TTGO setup tlacitko GPIO" + BoardProfile.SetupButtonPin + " inicializovano.");
                return true;
            }
            catch (Exception exception)
            {
                _initialized = false;
                Debug.WriteLine("TTGO setup tlacitko nelze inicializovat: " + exception.Message);
                return false;
            }
        }

        /// <summary>
        /// Returns true immediately when the setup button is pressed during boot.
        /// </summary>
        public bool IsPressedAtBoot()
        {
            return _initialized && IsPressed();
        }

        /// <summary>
        /// Returns true once when the setup button is pressed during runtime.
        /// </summary>
        public bool ConsumeSetupRequest()
        {
            if (!_initialized)
            {
                return false;
            }

            if (IsBlocked())
            {
                return false;
            }

            if (!IsPressed())
            {
                _requestConsumed = false;
                _ignoreUntilReleased = false;
                return false;
            }

            if (_ignoreUntilReleased)
            {
                return false;
            }

            if (!_requestConsumed)
            {
                _requestConsumed = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Ignores the currently held button until the user releases it.
        /// </summary>
        public void IgnoreCurrentPressUntilReleased()
        {
            if (!_initialized)
            {
                return;
            }

            if (IsPressed())
            {
                _ignoreUntilReleased = true;
                _requestConsumed = true;
                Debug.WriteLine("TTGO setup tlacitko ceka na pusteni pred dalsi akci.");
            }
            else
            {
                _ignoreUntilReleased = false;
                _requestConsumed = IsBlocked();
            }
        }

        /// <summary>
        /// Temporarily ignores setup button clicks during mode transitions.
        /// </summary>
        public void BlockSetupRequestsFor(int milliseconds)
        {
            if (milliseconds <= 0)
            {
                _blockedUntilTicks = 0;
                return;
            }

            _blockedUntilTicks = DateTime.UtcNow.Ticks + (milliseconds * TimeSpan.TicksPerMillisecond);
            _requestConsumed = true;
            Debug.WriteLine("TTGO setup tlacitko blokovano na ms: " + milliseconds);
        }

        /// <summary>
        /// Waits until the currently held setup button is released, or timeout expires.
        /// </summary>
        public void WaitUntilReleased(int timeoutMilliseconds)
        {
            int elapsed = 0;
            while (_initialized && IsPressed() && elapsed < timeoutMilliseconds)
            {
                Thread.Sleep(25);
                elapsed += 25;
            }

            if (_initialized && IsPressed())
            {
                _ignoreUntilReleased = true;
                _requestConsumed = true;
                return;
            }

            _ignoreUntilReleased = false;
            _requestConsumed = IsBlocked();
        }

        private bool IsBlocked()
        {
            if (_blockedUntilTicks == 0)
            {
                return false;
            }

            if (DateTime.UtcNow.Ticks < _blockedUntilTicks)
            {
                return true;
            }

            _blockedUntilTicks = 0;
            return false;
        }

        private bool IsPressed()
        {
            try
            {
                return _gpioController.Read(BoardProfile.SetupButtonPin) == PinValue.Low;
            }
            catch (Exception exception)
            {
                Debug.WriteLine("TTGO setup tlacitko cteni selhalo: " + exception.Message);
                return false;
            }
        }
    }
}
