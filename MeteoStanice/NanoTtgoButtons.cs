using System;
using System.Device.Gpio;
using System.Diagnostics;
using System.Threading;

namespace MeteoStanice
{
    /// <summary>
    /// Reads TTGO T-Display buttons and detects long setup presses.
    /// </summary>
    public sealed class NanoTtgoButtons
    {
        private GpioController _gpioController;
        private bool _initialized;
        private long _pressedSinceMilliseconds;
        private bool _requestConsumed;

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
        /// Blocks briefly after boot and returns true when the setup button is held.
        /// </summary>
        public bool IsHeldAtBoot(int holdMilliseconds)
        {
            if (!_initialized)
            {
                return false;
            }

            int elapsed = 0;
            while (elapsed < holdMilliseconds)
            {
                if (!IsPressed())
                {
                    return false;
                }

                Thread.Sleep(100);
                elapsed += 100;
            }

            return true;
        }

        /// <summary>
        /// Returns true once when the setup button has been held long enough during runtime.
        /// </summary>
        public bool ConsumeSetupRequest(int holdMilliseconds)
        {
            if (!_initialized)
            {
                return false;
            }

            long now = DateTime.UtcNow.Ticks / 10000;
            if (!IsPressed())
            {
                _pressedSinceMilliseconds = 0;
                _requestConsumed = false;
                return false;
            }

            if (_pressedSinceMilliseconds == 0)
            {
                _pressedSinceMilliseconds = now;
                return false;
            }

            if (!_requestConsumed && now - _pressedSinceMilliseconds >= holdMilliseconds)
            {
                _requestConsumed = true;
                return true;
            }

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
