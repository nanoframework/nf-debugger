//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.PortTcpIp
{
    public class NanoNetworkDevice : IDisposable
    {
        private readonly object _lock = new object();

        private NetworkStream _networkStream;

        private TcpClient _networkClient;

        public const int SafeDefaultTimeout = 1000;

        /// <summary>
        /// Maximum time, in milliseconds, to establish the TCP connection.
        /// </summary>
        public const int ConnectTimeout = 3000;

        /// <summary>
        /// Maximum time, in milliseconds, to send data to the device.
        /// </summary>
        public const int WriteTimeout = 5000;

        // keepalive: the first probe is sent after the connection is idle for this time (seconds), then at this interval, this number of times
        private const int KeepAliveTime = 5;
        private const int KeepAliveInterval = 2;
        private const int KeepAliveRetryCount = 3;

        /// <summary>
        /// <see langword="true"/> if there is a connection to the device and it is still alive.
        /// </summary>
        public bool Connected => AvailableBytes >= 0;

        /// <summary>
        /// Number of bytes that can be read, or -1 if the device isn't connected or the connection was closed by the other side.
        /// </summary>
        public int AvailableBytes
        {
            get
            {
                try
                {
                    var socket = _networkClient?.Client;

                    if (socket is null || !socket.Connected)
                    {
                        return -1;
                    }

                    var available = socket.Available;

                    if (available > 0)
                    {
                        return available;
                    }

                    if (!socket.Poll(0, SelectMode.SelectRead))
                    {
                        return 0;
                    }

                    // readable: data may have arrived since the first read, check again
                    available = socket.Available;

                    // readable without data available means the other side closed (or reset) the connection
                    return available > 0 ? available : -1;
                }
                catch (Exception ex) when (ex is ObjectDisposedException || ex is SocketException || ex is NullReferenceException)
                {
                    // net472 throws when the client was disposed while the property was being read
                    return -1;
                }
            }
        }

        public NetworkDeviceInformation NetworkDeviceInformation { get; set; }

        /// <summary>
        /// Connects to the device, or returns the stream of the current connection if there is one.
        /// </summary>
        /// <exception cref="TimeoutException">The connection wasn't established in <see cref="ConnectTimeout"/> ms.</exception>
        public NetworkStream Connect()
        {
            lock (_lock)
            {
                if (_networkClient is not null && Connected)
                {
                    return _networkStream;
                }

                // get rid of any previous connection that is no longer alive
                CloseInternal();

                var client = new TcpClient
                {
                    NoDelay = true,
                    SendTimeout = WriteTimeout,
                    ReceiveTimeout = SafeDefaultTimeout
                };

                try
                {
                    var connect = client.ConnectAsync(NetworkDeviceInformation.Host, NetworkDeviceInformation.Port);

                    if (!connect.Wait(ConnectTimeout))
                    {
                        // observe the eventual failure of the abandoned connect
                        connect.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);

                        throw new TimeoutException(
                            $"Timeout connecting to {NetworkDeviceInformation.DeviceId}.");
                    }

                    ConfigureKeepAlive(client.Client);

                    var stream = client.GetStream();
                    stream.WriteTimeout = WriteTimeout;
                    stream.ReadTimeout = SafeDefaultTimeout;

                    _networkClient = client;
                    _networkStream = stream;

                    return _networkStream;
                }
                catch
                {
                    client.Dispose();

                    throw;
                }
            }
        }

        private static void ConfigureKeepAlive(Socket socket)
        {
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            try
            {
#if NET5_0_OR_GREATER
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveTime, KeepAliveTime);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveInterval, KeepAliveInterval);
                socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.TcpKeepAliveRetryCount, KeepAliveRetryCount);
#else
                // struct tcp_keepalive: on/off, time (ms), interval (ms)
                var keepAlive = new byte[12];
                BitConverter.GetBytes(1).CopyTo(keepAlive, 0);
                BitConverter.GetBytes(KeepAliveTime * 1000).CopyTo(keepAlive, 4);
                BitConverter.GetBytes(KeepAliveInterval * 1000).CopyTo(keepAlive, 8);

                socket.IOControl(IOControlCode.KeepAliveValues, keepAlive, null);
#endif
            }
            catch (Exception ex) when (ex is SocketException || ex is PlatformNotSupportedException || ex is NotSupportedException)
            {
                // the fine tuning of the keepalive isn't available in this platform: the system defaults will do
            }
        }

        public void Close()
        {
            lock (_lock)
            {
                CloseInternal();
            }
        }

        // must be called while holding _lock
        private void CloseInternal()
        {
            var stream = _networkStream;
            var client = _networkClient;

            // the fields are cleared first: on net472 reading Connected after a dispose throws
            _networkStream = null;
            _networkClient = null;

            try
            {
                stream?.Dispose();
            }
            catch
            {
                // nothing to do if the stream can't be closed
            }

            try
            {
                client?.Dispose();
            }
            catch
            {
                // nothing to do if the client can't be closed
            }
        }

        public void Dispose()
        {
            Close();
        }
    }
}
