// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
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
        private readonly int _discoveryPort;
        private volatile bool _started = false;
        private volatile Thread _threadWatch = null;
        private UdpClient _udpClient;
        private readonly PortTcpIpManager _ownerManager;
        private readonly object _lifecycleLock = new object();
        private bool _disposed = false;

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
                    // once disposed, Start() has no effect
                    if (_started || _disposed)
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
            LogMessage($"PortTcpIp network watcher started @ Thread {Environment.CurrentManagedThreadId} [ProcessID: {CurrentProcessId}]");

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

                        var message = Encoding.ASCII.GetString(discoveryPacket);

                        ProcessDiscoveryMessage(message, remoteEndPoint.Address, isDiscovering.Token);
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

            LogMessage($"PortTcpIp device watcher stopped @ Thread {Environment.CurrentManagedThreadId}");
        }

        private bool IsCurrentWatcherThread => _threadWatch == Thread.CurrentThread;

#if NET5_0_OR_GREATER
        private static int CurrentProcessId => Environment.ProcessId;
#else
        private static int CurrentProcessId => System.Diagnostics.Process.GetCurrentProcess().Id;
#endif

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

        // devices being added: they re-announce every few seconds, no need to queue one more attempt while one is running.
        // The key is the device id, or the port for a device announced from one of the addresses of this machine
        private readonly ConcurrentDictionary<string, byte> _adding = new ConcurrentDictionary<string, byte>();

        // port -> id of the device that was announced from one of the addresses of this machine
        private readonly ConcurrentDictionary<int, string> _localDevices = new ConcurrentDictionary<int, string>();

        /// <summary>
        /// Tells if an address is one of this machine. A device on this machine announces itself from each of the adapters it
        /// sends to, but it is one device: those announcements are merged.
        /// </summary>
        internal Func<IPAddress, bool> IsLocalAddress { get; set; } = LocalAddresses.IsLocal;

        private static string LocalKey(int port) => $"local:{port}";

        /// <summary>
        /// Decides if an announcement from one of the addresses of this machine is for a new device.
        /// </summary>
        private bool ClaimLocalPort(NetworkDeviceInformation info)
        {
            if (PortTcpIpManager.GetRegisteredDevice(info) is not null)
            {
                return false;
            }

            if (_localDevices.TryRemove(info.Port, out var knownId)
                && NetworkDeviceInformation.TryParse(knownId, out var known)
                && PortTcpIpManager.GetRegisteredDevice(known) is not null)
            {
                if (IPAddress.TryParse(known.Host, out var knownAddress)
                    && IsLocalAddress(knownAddress))
                {
                    // the same device, announced from another adapter
                    _localDevices[info.Port] = knownId;

                    return false;
                }

                // the address that the device was listed under is gone, and it announces from this one
                Removed?.Invoke(this, known);
            }

            return true;
        }

        internal void ProcessDiscoveryMessage(string message, IPAddress sender, CancellationToken isDiscovering)
        {
            if (!DiscoveryMessage.TryParse(message, out var discovery))
            {
                return;
            }

            // the address of the device is the one of the sender of the announcement
            var info = discovery.ToDeviceInformation(sender);
            var local = IsLocalAddress(sender);

            if (discovery.IsAnnouncement)
            {
                if (Added is null)
                {
                    return;
                }

                // claimed before looking at the list of devices: the device is listed before the claim is released,
                // so an announcement is either turned down here or sees the device listed
                var key = local ? LocalKey(info.Port) : info.DeviceId;

                if (!_adding.TryAdd(key, 0))
                {
                    return;
                }

                var adding = false;

                try
                {
                    if (local
                        ? ClaimLocalPort(info)
                        : PortTcpIpManager.GetRegisteredDevice(info) is null)
                    {
                        adding = true;

                        Task.Run(async () =>
                        {
                            try
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

                                if (local
                                    && PortTcpIpManager.GetRegisteredDevice(info) is not null)
                                {
                                    // the announcements from the other adapters are for this one
                                    _localDevices[info.Port] = info.DeviceId;
                                }
                            }
                            catch
                            {
                                // the next announcement tries again
                            }
                            finally
                            {
                                _adding.TryRemove(key, out _);
                            }
                        });
                    }
                }
                finally
                {
                    if (!adding)
                    {
                        _adding.TryRemove(key, out _);
                    }
                }
            }
            else
            {
                // wherever the device was listed from, the departure announced from any address of this machine is for it
                if (local
                    && _localDevices.TryRemove(info.Port, out var listedId)
                    && NetworkDeviceInformation.TryParse(listedId, out var listed))
                {
                    info = listed;
                }

                Removed?.Invoke(this, info);
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
            lock (_lifecycleLock)
            {
                // from now on Start() has no effect
                _disposed = true;
            }

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
