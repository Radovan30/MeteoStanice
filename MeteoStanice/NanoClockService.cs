using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using nanoFramework.Runtime.Native;

namespace MeteoStanice
{
    /// <summary>
    /// Keeps station time running and periodically synchronizes it from NTP.
    /// </summary>
    public sealed class NanoClockService
    {
        private const string DefaultNtpServer = "pool.ntp.org";
        private const int NtpPort = 123;
        private const int NtpPacketLength = 48;
        private const int SocketTimeoutMilliseconds = 4000;
        private const int SynchronizationIntervalSeconds = 21600;
        private const int RetryIntervalSeconds = 900;
        // Until the first successful synchronization no samples are created, so retry often.
        private const int UnsynchronizedRetryIntervalSeconds = 60;
        private const int ClockTrustSeconds = 86400;
        private const int MinimumValidYear = 2025;
        private static readonly DateTime NtpEpoch = new DateTime(1900, 1, 1, 0, 0, 0);

        private DateTime _lastSynchronizationUtc;
        private DateTime _nextSynchronizationUtc;
        private bool _synchronized;

        /// <summary>
        /// Returns true while the clock was synchronized recently enough.
        /// </summary>
        public bool IsSynchronized()
        {
            if (!_synchronized)
            {
                return false;
            }

            return (DateTime.UtcNow - _lastSynchronizationUtc).TotalSeconds <= ClockTrustSeconds;
        }

        /// <summary>
        /// Returns true once the clock was synchronized at least once since boot and shows a plausible year.
        /// The RTC keeps running between synchronizations, so an expired trust window does not invalidate it.
        /// Samples must not be created before this is true, otherwise they get timestamps from 1970.
        /// </summary>
        public bool HasValidTime()
        {
            return _synchronized && DateTime.UtcNow.Year >= MinimumValidYear;
        }

        /// <summary>
        /// True after the first successful NTP synchronization since boot.
        /// </summary>
        public bool HasEverSynchronized
        {
            get { return _synchronized; }
        }

        /// <summary>
        /// Synchronizes immediately when Wi-Fi is connected.
        /// </summary>
        public bool TrySynchronize(bool wifiConnected)
        {
            return TrySynchronize(wifiConnected, true);
        }

        /// <summary>
        /// Synchronizes when the configured interval or retry delay has elapsed.
        /// </summary>
        public bool TrySynchronizeIfDue(bool wifiConnected)
        {
            return TrySynchronize(wifiConnected, false);
        }

        /// <summary>
        /// Builds display date text in local station time.
        /// </summary>
        public string GetDisplayDate()
        {
            DateTime localTime = ToPragueTime(DateTime.UtcNow);
            return Two(localTime.Day) + "." + Two(localTime.Month) + "." + localTime.Year;
        }

        /// <summary>
        /// Builds display time text in local station time.
        /// </summary>
        public string GetDisplayTime()
        {
            return GetDisplayTime(DateTime.UtcNow);
        }

        /// <summary>
        /// Builds display time text for a specific UTC timestamp in local station time.
        /// </summary>
        public string GetDisplayTime(DateTime utcTime)
        {
            DateTime localTime = ToPragueTime(utcTime);
            return Two(localTime.Hour) + ":" + Two(localTime.Minute);
        }

        private bool TrySynchronize(bool wifiConnected, bool force)
        {
            if (!wifiConnected)
            {
                return false;
            }

            DateTime now = DateTime.UtcNow;
            if (!force && _nextSynchronizationUtc != DateTime.MinValue && now < _nextSynchronizationUtc)
            {
                return IsSynchronized();
            }

            try
            {
                DateTime ntpTimeUtc = ReadNetworkTimeUtc(DefaultNtpServer);
                Rtc.SetSystemTime(ntpTimeUtc);
                _lastSynchronizationUtc = ntpTimeUtc;
                _nextSynchronizationUtc = ntpTimeUtc.AddSeconds(SynchronizationIntervalSeconds);
                _synchronized = true;
                Debug.WriteLine("Clock synchronized UTC: " + FormatDateTime(ntpTimeUtc));
                return true;
            }
            catch (Exception exception)
            {
                _nextSynchronizationUtc = now.AddSeconds(_synchronized ? RetryIntervalSeconds : UnsynchronizedRetryIntervalSeconds);
                Debug.WriteLine("Clock synchronization failed: " + exception.Message);
                return false;
            }
        }

        private static DateTime ReadNetworkTimeUtc(string hostName)
        {
            IPHostEntry hostEntry = Dns.GetHostEntry(hostName);
            if (hostEntry == null || hostEntry.AddressList == null || hostEntry.AddressList.Length == 0)
            {
                throw new Exception("NTP DNS nema adresu.");
            }

            byte[] request = new byte[NtpPacketLength];
            request[0] = 0x1B;
            byte[] response = new byte[NtpPacketLength];
            EndPoint remoteEndPoint = new IPEndPoint(hostEntry.AddressList[0], NtpPort);
            Socket socket = null;

            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                socket.SendTimeout = SocketTimeoutMilliseconds;
                socket.ReceiveTimeout = SocketTimeoutMilliseconds;
                socket.SendTo(request, remoteEndPoint);
                int received = socket.Receive(response);
                if (received < NtpPacketLength)
                {
                    throw new Exception("NTP odpoved je kratka.");
                }

                ulong seconds = ((ulong)response[40] << 24)
                    | ((ulong)response[41] << 16)
                    | ((ulong)response[42] << 8)
                    | response[43];
                return NtpEpoch.AddSeconds(seconds);
            }
            finally
            {
                if (socket != null)
                {
                    socket.Close();
                }
            }
        }

        /// <summary>
        /// Converts UTC to Central European time (CET, UTC+1) with the EU summer time rule
        /// (CEST, UTC+2 from the last Sunday in March 01:00 UTC to the last Sunday in October 01:00 UTC).
        /// </summary>
        public static DateTime ToPragueTime(DateTime utcTime)
        {
            DateTime summerStart = LastSundayOf(utcTime.Year, 3).AddHours(1);
            DateTime summerEnd = LastSundayOf(utcTime.Year, 10).AddHours(1);
            bool summerTime = utcTime.Ticks >= summerStart.Ticks && utcTime.Ticks < summerEnd.Ticks;
            return utcTime.AddHours(summerTime ? 2 : 1);
        }

        private static DateTime LastSundayOf(int year, int month)
        {
            // March and October both have 31 days.
            DateTime lastDay = new DateTime(year, month, 31, 0, 0, 0);
            return lastDay.AddDays(-(int)lastDay.DayOfWeek);
        }

        private static string FormatDateTime(DateTime value)
        {
            return Two(value.Day) + "." + Two(value.Month) + "." + value.Year + " " + Two(value.Hour) + ":" + Two(value.Minute) + ":" + Two(value.Second);
        }

        private static string Two(int value)
        {
            return value < 10 ? "0" + value : value.ToString();
        }
    }
}
