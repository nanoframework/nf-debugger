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
        private volatile bool _started = false;
        private volatile Thread _threadWatch = null;
        private UdpClient _udpClient;
        private readonly PortTcpIpManager _ownerManager;
        private readonly object _lifecycleLock = new object();

        public delegate void EventDeviceAdded(object sender, NetworkDeviceInformation deviceInfo);

        public event EventDeviceAdded Added;

        public delegate void EventDeviceRemoved(object sender, NetworkDeviceInformation deviceInfo);

        public event EventDeviceRemoved Removed;

        public DeviceWatcherStatus Status { get; internal set; }

        /// <summary>
        /// Constructor for a <see cref="PortTcpIpManager"/> network watcher class.
        /// </summary>
        /// <param name="owner">The <see cref="PortTcpIpManager"/> that owns this network watcher.</param>
        /// <param name="discoveryPort">The port what will be listening for nanoDevice announcement packets.</param>
        public DeviceWatcher(
            PortTcpIpManager owner,
            int discoveryPort)
        {
            _discoveryPort = discoveryPort;
            _ownerManager = owner;
        }

        /// <summary>
        /// Stops the watcher.
        /// </summary>
        /// <remarks>
        /// This call doesn't wait for the watcher to stop. The <see cref="Status"/> changes to
        /// <see cref="DeviceWatcherStatus.Stopped"/> once the watcher has actually stopped.
        /// </remarks>
        public void Stop()
        {
            lock (_lifecycleLock)
            {
                if (!_started)
                {
                    // never started or already stopping/stopped: don't overwrite the current status
                    return;
                }

                Status = DeviceWatcherStatus.Stopping;
                _started = false;

                _udpClient?.Close();
            }
        }

        /// <summary>
        /// Starts the watcher.
        /// </summary>
        public void Start()
        {
            while (true)
            {
                Thread previousThread;

                lock (_lifecycleLock)
                {
                    if (_started)
                    {
                        return;
                    }

                    previousThread = _threadWatch;

                    if (previousThread is null
                        || !previousThread.IsAlive
                        || previousThread == Thread.CurrentThread)
                    {
                        StartWatcherThread();

                        return;
                    }
                }

                previousThread.Join();
            }
        }

        // must be called while holding _lifecycleLock
        private void StartWatcherThread()
        {
            try
            {
                _threadWatch = new Thread(WatcherThread)
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Lowest
                };

                Status = DeviceWatcherStatus.Started;
                _started = true;

                _threadWatch.Start();
            }
            catch
            {
                _started = false;
                _threadWatch = null;
                Status = DeviceWatcherStatus.Stopped;

                throw;
            }
        }

        private void WatcherThread()
        {
            try
            {
                RunWatcher();
            }
            finally
            {
                lock (_lifecycleLock)
                {
                    if (IsCurrentWatcherThread)
                    {
                        Status = DeviceWatcherStatus.Stopped;
                    }
                }
            }
        }

        private void RunWatcher()
        {
            LogMessage($"PortTcpIp network watcher started @ Thread {Thread.CurrentThread.ManagedThreadId} [ProcessID: {Process.GetCurrentProcess().Id}]");

            UdpClient udpClient = null;
            string listenError = null;

            lock (_lifecycleLock)
            {
                if (!_started || !IsCurrentWatcherThread)
                {
                    return;
                }

                try
                {
                    udpClient = new UdpClient();
                    udpClient.ExclusiveAddressUse = false;
                    udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

                    IPEndPoint listeningPort = new IPEndPoint(IPAddress.Any, _discoveryPort);

                    udpClient.Client.Bind(listeningPort);
                }
                catch (Exception ex)
                {
                    udpClient?.Dispose();
                    udpClient = null;

                    _started = false;

                    listenError = ex.Message;
                }

                _udpClient = udpClient;
            }

            if (udpClient is null)
            {
                LogMessage($"PortTcpIp network watcher failed to listen on port {_discoveryPort}: {listenError}");

                return;
            }

            using var isDiscovering = new CancellationTokenSource();

            try
            {
                while (_started && IsCurrentWatcherThread)
                {
                    try
                    {
                        IPEndPoint remoteEndPoint = null;

                        var discoveryPacket = udpClient.Receive(ref remoteEndPoint);

                        // get address from device
                        // TODO
                        // remoteEndPoint;

                        var message = Encoding.ASCII.GetString(discoveryPacket);

                        ProcessDiscoveryMessage(message, isDiscovering.Token);
                    }
#if DEBUG
                    catch (Exception ex)
#else
                    catch
#endif
                    {
                        // catch all so the listener can be always listening
                    }
                }
            }
            finally
            {
                isDiscovering.Cancel();

                udpClient.Close();
            }

            LogMessage($"PortTcpIp device watcher stopped @ Thread {Thread.CurrentThread.ManagedThreadId}");
        }

        private bool IsCurrentWatcherThread => _threadWatch == Thread.CurrentThread;

        private void LogMessage(string message)
        {
            try
            {
                _ownerManager.OnLogMessageAvailable(message);
            }
            catch
            {
                // a faulty log handler must not prevent the watcher from running or stopping
            }
        }

        private void ProcessDiscoveryMessage(string message, CancellationToken isDiscovering)
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
                                // Force true async running
                                await Task.Yield();
                                var exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(info, cancellationToken: isDiscovering);
                                if (exclusiveAccess is not null)
                                {
                                    try
                                    {
                                        Added?.Invoke(this, info);
                                    }
                                    finally
                                    {
                                        exclusiveAccess.Dispose();
                                    }
                                }
                                ;
                            });
                        }
                    }
                    break;

                case CommandDeviceStop:
                    Removed?.Invoke(this, new NetworkDeviceInformation(host, port));
                    break;
            }
        }

        /// <summary>
        /// Stops the watcher and waits for the watcher thread to exit.
        /// </summary>
        /// <param name="millisecondsTimeout">Maximum time to wait, or <see cref="Timeout.Infinite"/>.</param>
        /// <returns><see langword="true"/> if the watcher thread is not running when this call returns.
        /// When called from the watcher thread itself (e.g. from an event handler) this doesn't wait and returns <see langword="false"/>.</returns>
        internal bool StopAndWait(int millisecondsTimeout)
        {
            Stop();

            var thread = _threadWatch;

            if (thread is null)
            {
                return true;
            }

            if (thread == Thread.CurrentThread)
            {
                // don't wait for our thread
                return false;
            }

            return thread.Join(millisecondsTimeout);
        }

        public void Dispose()
        {
            // stop the watcher and wait up to 3 seconds for it to be stopped
            bool stopped = StopAndWait(3000);

            lock (_lifecycleLock)
            {
                _udpClient?.Dispose();

                if (stopped && _threadWatch?.IsAlive != true)
                {
                    _threadWatch = null;
                }
            }
        }
    }
}
