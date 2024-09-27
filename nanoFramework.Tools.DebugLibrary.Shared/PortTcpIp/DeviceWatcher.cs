// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger.NFDevice;

namespace nanoFramework.Tools.Debugger.PortTcpIp
{
    public class DeviceWatcher : IDisposable
    {
        private const char TokenSeparator = ':';
        private const string CommandDeviceStart = "+";
        private const string CommandDeviceStop = "-";

        private readonly int _discoveryPort;
        private bool _started = false;
        private Thread _threadWatch = null;
        private UdpClient _udpClient;
        private readonly PortTcpIpManager _ownerManager;
        private readonly AutoResetEvent _watcherStopped = new(false);

        public delegate void EventDeviceAdded(object sender, NetworkDeviceInformation deviceInfo);

        public event EventDeviceAdded Added;

        public delegate void EventDeviceRemoved(object sender, NetworkDeviceInformation deviceInfo);

        public event EventDeviceRemoved Removed;

        public DeviceWatcherStatus Status { get; internal set; }

        /// <summary>
        /// Constructor for a <see cref="PortTcpIpManager"/> network watcher class.
        /// </summary>
        /// <param name="owner"><The <see cref="PortTcpIpManager"/> that owns this network watcher./param>
        /// <param name="discoveryPort">The port what will be listening for nanoDevice announcement packets.</param>
        public DeviceWatcher(
            PortTcpIpManager owner,
            int discoveryPort)
        {
            _discoveryPort = discoveryPort;
            _ownerManager = owner;
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
                    _ownerManager.OnLogMessageAvailable($"PortTcpIp network watcher started @ Thread {_threadWatch.ManagedThreadId} [ProcessID: {Process.GetCurrentProcess().Id}]");

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

                    _ownerManager.OnLogMessageAvailable($"PortTcpIp device watcher stopped @ Thread {_threadWatch.ManagedThreadId}");

                    Status = DeviceWatcherStatus.Stopped;

                    // signal watcher stopped
                    _watcherStopped.Set();

                })
                {
                    IsBackground = true,
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
                    if (Added is not null)
                    {
                        var info = new NetworkDeviceInformation(host, port);
                        if (PortTcpIpManager.GetRegisteredDevice(info) is null)
                        {
                            Task.Run(async () =>
                            {
                                await Task.Yield(); // Force true async running
                                GlobalExclusiveDeviceAccess.CommunicateWithDevice(info, () =>
                                {
                                    Added.Invoke(this, info);
                                });
                            });
                        }
                    }
                    break;

                case CommandDeviceStop:
                    Removed?.Invoke(this, new NetworkDeviceInformation(host, port));
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
