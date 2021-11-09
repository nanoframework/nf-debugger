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
                _udpClient = new UdpClient(_discoveryPort);

                IPEndPoint listeningPort = new IPEndPoint(IPAddress.Any, _discoveryPort);

                _threadWatch = new Thread(() =>
                {
                    _started = true;

                    Status = DeviceWatcherStatus.Started;

                    while (_started)
                    {
                        var message = Encoding.ASCII.GetString(_udpClient.Receive(ref listeningPort));
                        ProcessDiscoveryMessage(message);
                    }

                    Status = DeviceWatcherStatus.Stopped;

                // signal watcher stopped
                _watcherStopped.Set();

                })
                {
                    Priority = ThreadPriority.Lowest
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

            var tokens = message.Split(new[] {TokenSeparator});

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
