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

        public delegate void EventDeviceAdded(object sender, NetworkNanoDeviceInformation deviceInfo);

        public event EventDeviceAdded Added;

        public delegate void EventDeviceRemoved(object sender, NetworkNanoDeviceInformation deviceInfo);

        public event EventDeviceRemoved Removed;

        public DeviceWatcherStatus Status { get; internal set; }

        public NetworkWatcher(int discoveryPort)
        {
            _discoveryPort = discoveryPort;
        }

        public void Stop()
        {
            _udpClient.Close();
            _started = false;
            Status = DeviceWatcherStatus.Stopping;
        }

        public void Start()
        {
            _udpClient = new UdpClient();
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _discoveryPort));
            if (_started) return;

            _threadWatch = new Thread(() =>
            {
                _started = true;

                Status = DeviceWatcherStatus.Started;

                var from = new IPEndPoint(0, 0);

                while (_started)
                {
                    var message = Encoding.ASCII.GetString(_udpClient.Receive(ref from));
                    ProcessDiscoveryMessage(message);
                }

                Status = DeviceWatcherStatus.Stopped;
            })
            {
                Priority = ThreadPriority.Lowest
            };
            _threadWatch.Start();
        }

        private void ProcessDiscoveryMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return;

            var tokens = message.Split(new[] {TokenSeparator});
            if (tokens.Length < 3)
                return;

            var command = tokens[0];
            var host = tokens[1];

            if (!int.TryParse(tokens[2], out var port))
                return;
            
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
            Stop();

            while (Status != DeviceWatcherStatus.Started)
            {
                Thread.Sleep(50);
            }

            _udpClient.Dispose();
            _threadWatch = null;
        }
    }
}