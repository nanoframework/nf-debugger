//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using nanoFramework.Tools.Debugger.PortSerial;

namespace nanoFramework.Tools.Debugger.PortTcpIp
{
    public class NetworkWatcher : IDisposable
    {
        private const char TokenSeparator = ':';
        private const string CommandDeviceStart = "+";
        private const string CommandDeviceStop = "-";

        private readonly int _discoveryPort;
        private bool _started = false;
        private Thread _threadWatch = null;
        private UdpClient _udpClient;

        private readonly AutoResetEvent _watcherStopped = new(false);

        public delegate void EventDeviceAdded(object sender, NetworkNanoDeviceInformation deviceInfo);

        public event EventDeviceAdded Added;

        public delegate void EventDeviceRemoved(object sender, NetworkNanoDeviceInformation deviceInfo);

        public event EventDeviceRemoved Removed;

        public DeviceWatcherStatus Status { get; internal set; }

        public NetworkWatcher(int discoveryPort)
        {
            _discoveryPort = discoveryPort;
        }

        public NetworkWatcher()
        {
        }

        public void Stop()
        {
            // can stop only if it was started
            if (_udpClient != null)
            {
                _udpClient.Close();

                _started = false;

                Status = DeviceWatcherStatus.Stopping;
            }
        }

        public void Start()
        {
            if (!_started)
            {
                _threadWatch = new Thread(async () =>
                {
                    _udpClient = new UdpClient();
                    _udpClient.ExclusiveAddressUse = false;
                    _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                    IPEndPoint listeningPort = new IPEndPoint(IPAddress.Any, _discoveryPort);

                    _udpClient.Client.Bind(listeningPort);

                    _started = true;

                    Status = DeviceWatcherStatus.Started;

                    while (_started)
                    {
                        try
                        {
                            var discoveryPacket = await _udpClient.ReceiveAsync();

                            // get address from device
                            // TODO
                            // discoveryPacket.RemoteEndPoint;

                            var message = Encoding.ASCII.GetString(discoveryPacket.Buffer);

                            ProcessDiscoveryMessage(message);
                        }
#if DEBUG
                        catch (Exception ex)
#else
                        catch
#endif
                        {
                            // catch all so the listener can be always listening
                            // on exception caused by the socket being closed, the thread will exit on the while loop condition
                        }
                    }

                    Status = DeviceWatcherStatus.Stopped;

                    // signal watcher stopped
                    _watcherStopped.Set();

                })
                {
                    IsBackground = true
                };

                _threadWatch.Start();
            }
        }

        private void ProcessDiscoveryMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return;
            }

            var tokens = message.Split(new[] { TokenSeparator });

            if (tokens.Length < 3)
            {
                return;
            }

            var command = tokens[0];
            var host = tokens[1];

            if (!int.TryParse(tokens[2], out var port))
            {
                return;
            }

            switch (command)
            {
                case CommandDeviceStart:
                    Added?.Invoke(this, new NetworkNanoDeviceInformation(host, port));
                    break;

                case CommandDeviceStop:
                    Removed?.Invoke(this, new NetworkNanoDeviceInformation(host, port));
                    break;
            }
        }

        public void Dispose()
        {
            // try stop the watcher
            Stop();

            // wait 3 seconds for the watcher to be stopped
            _watcherStopped.WaitOne(TimeSpan.FromSeconds(3));

            _udpClient?.Dispose();

            _threadWatch = null;
        }
    }
}
