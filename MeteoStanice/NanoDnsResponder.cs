using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace MeteoStanice
{
    /// <summary>
    /// Resolves the local provisioning host name to the ESP32 SoftAP address.
    /// </summary>
    public sealed class NanoDnsResponder
    {
        // Standard UDP port used by DNS.
        private const int DnsPort = 53;
        // Small DNS buffer is enough for one local A-record question.
        private const int BufferSize = 512;

        private Socket _socket;
        private Thread _thread;
        private string _hostName;
        private string _address;
        private bool _running;

        /// <summary>
        /// Starts the UDP DNS responder.
        /// </summary>
        public bool Start(string hostName, string address)
        {
            if (hostName == null || hostName.Length == 0 || address == null || address.Length == 0)
            {
                Debug.WriteLine("DNS responder skipped: host nebo adresa chybi.");
                return false;
            }

            try
            {
                _hostName = NormalizeName(hostName);
                _address = address;
                _running = true;
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.ReceiveTimeout = 500;
                _socket.Bind(new IPEndPoint(IPAddress.Any, DnsPort));
                _thread = new Thread(Run);
                _thread.Start();
                Debug.WriteLine("DNS responder bezi: " + _hostName + " -> " + _address);
                return true;
            }
            catch (Exception exception)
            {
                Debug.WriteLine("DNS responder nelze spustit: " + exception.Message);
                Stop();
                return false;
            }
        }

        /// <summary>
        /// Stops the UDP DNS responder.
        /// </summary>
        public void Stop()
        {
            _running = false;

            if (_socket != null)
            {
                try
                {
                    _socket.Close();
                }
                catch
                {
                }

                _socket = null;
            }
        }

        private void Run()
        {
            byte[] buffer = new byte[BufferSize];

            while (_running && _socket != null)
            {
                try
                {
                    if (!_socket.Poll(500000, SelectMode.SelectRead))
                    {
                        continue;
                    }

                    EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    int length = _socket.ReceiveFrom(buffer, 0, buffer.Length, SocketFlags.None, ref remoteEndPoint);
                    byte[] response = BuildResponse(buffer, length);
                    if (response != null)
                    {
                        _socket.SendTo(response, response.Length, SocketFlags.None, remoteEndPoint);
                    }
                }
                catch
                {
                }
            }
        }

        /// <summary>
        /// Builds a minimal DNS A-record response for the configured host name.
        /// </summary>
        private byte[] BuildResponse(byte[] request, int length)
        {
            if (request == null || length < 12)
            {
                return null;
            }

            int questionOffset = 12;
            string requestedName = ReadQuestionName(request, length, questionOffset);
            if (requestedName == null || requestedName != _hostName)
            {
                return null;
            }

            int questionEnd = FindQuestionEnd(request, length, questionOffset);
            if (questionEnd < 0 || questionEnd + 4 > length)
            {
                return null;
            }

            if (!IsAddressQuestion(request, questionEnd))
            {
                return null;
            }

            int questionLength = questionEnd + 4 - questionOffset;
            byte[] response = new byte[12 + questionLength + 16];

            CopyBytes(request, 0, response, 0, 2);
            response[2] = 0x81;
            response[3] = 0x80;
            response[4] = 0x00;
            response[5] = 0x01;
            response[6] = 0x00;
            response[7] = 0x01;
            response[8] = 0x00;
            response[9] = 0x00;
            response[10] = 0x00;
            response[11] = 0x00;
            CopyBytes(request, questionOffset, response, 12, questionLength);

            int answerOffset = 12 + questionLength;
            response[answerOffset] = 0xC0;
            response[answerOffset + 1] = 0x0C;
            response[answerOffset + 2] = 0x00;
            response[answerOffset + 3] = 0x01;
            response[answerOffset + 4] = 0x00;
            response[answerOffset + 5] = 0x01;
            response[answerOffset + 6] = 0x00;
            response[answerOffset + 7] = 0x00;
            response[answerOffset + 8] = 0x00;
            response[answerOffset + 9] = 0x1E;
            response[answerOffset + 10] = 0x00;
            response[answerOffset + 11] = 0x04;
            WriteIpAddress(response, answerOffset + 12, _address);

            return response;
        }

        /// <summary>
        /// Checks whether the DNS question asks for an IPv4 A record.
        /// </summary>
        private static bool IsAddressQuestion(byte[] request, int questionEnd)
        {
            return request[questionEnd] == 0x00
                && request[questionEnd + 1] == 0x01
                && request[questionEnd + 2] == 0x00
                && request[questionEnd + 3] == 0x01;
        }

        /// <summary>
        /// Reads the first DNS question name.
        /// </summary>
        private static string ReadQuestionName(byte[] request, int length, int offset)
        {
            string name = string.Empty;
            int index = offset;

            while (index < length)
            {
                int labelLength = request[index];
                if (labelLength == 0)
                {
                    return NormalizeName(name);
                }

                index++;
                if (index + labelLength > length)
                {
                    return null;
                }

                if (name.Length > 0)
                {
                    name += ".";
                }

                for (int labelIndex = 0; labelIndex < labelLength; labelIndex++)
                {
                    name += (char)request[index + labelIndex];
                }

                index += labelLength;
            }

            return null;
        }

        /// <summary>
        /// Finds the zero terminator after the DNS question name.
        /// </summary>
        private static int FindQuestionEnd(byte[] request, int length, int offset)
        {
            int index = offset;
            while (index < length)
            {
                int labelLength = request[index];
                if (labelLength == 0)
                {
                    return index + 1;
                }

                index += labelLength + 1;
            }

            return -1;
        }

        /// <summary>
        /// Normalizes host names for case-insensitive comparison.
        /// </summary>
        private static string NormalizeName(string name)
        {
            if (name == null)
            {
                return string.Empty;
            }

            string normalized = name.ToLower();
            if (normalized.EndsWith("."))
            {
                normalized = normalized.Substring(0, normalized.Length - 1);
            }

            return normalized;
        }

        /// <summary>
        /// Copies bytes without using APIs that are sometimes missing on constrained firmware.
        /// </summary>
        private static void CopyBytes(byte[] source, int sourceOffset, byte[] destination, int destinationOffset, int count)
        {
            for (int index = 0; index < count; index++)
            {
                destination[destinationOffset + index] = source[sourceOffset + index];
            }
        }

        /// <summary>
        /// Writes an IPv4 string into DNS response bytes.
        /// </summary>
        private static void WriteIpAddress(byte[] buffer, int offset, string address)
        {
            string[] parts = address.Split('.');
            for (int index = 0; index < 4; index++)
            {
                buffer[offset + index] = (byte)int.Parse(parts[index]);
            }
        }
    }
}
