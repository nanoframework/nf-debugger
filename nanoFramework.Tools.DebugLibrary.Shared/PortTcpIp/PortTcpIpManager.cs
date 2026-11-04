// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger.NFDevice;
using nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.Debugger.PortTcpIp
{
    public class PortTcpIpManager : PortBase
    {
        /// <summary>
        /// Port listening for announcement messages from nanoDevices in the local network.
        /// </summary>
        public const int DiscoveryPort = 23657;

        private readonly DeviceWatcher _deviceWatcher;

        // Network device watchers started flag
        private bool _watchersStarted = false;

        // the device watcher auto start (requested in the constructor) no longer applies
        private bool _autoStartCancelled = false;
        private readonly object _autoStartLock = new object();

        private volatile bool _disposed = false;

        /// <summary>
        /// Internal list with the actual nF Network devices.
        /// </summary>
        /// <remarks>
        /// This must be a static list because the <see cref="NanoDeviceCollection"/> in <see cref="PortBase.NanoFrameworkDevices"/>
        /// is global (<see cref="NanoDeviceCollection.Instance"/>).
        /// Every item in <see cref="_networkDevices"/> must have its device in <see cref="PortBase.NanoFrameworkDevices"/>, and
        /// every network device in <see cref="PortBase.NanoFrameworkDevices"/> must have its item in <see cref="_networkDevices"/>.
        /// To keep both lists in sync, read and change them while holding the lock taken with <see cref="NanoDeviceCollection.Lock"/>.
        /// </remarks>
        private static readonly List<NetworkDeviceInformation> _networkDevices = new List<NetworkDeviceInformation>();

        // NanoFrameworkDevices is global and can hold devices of other transports
        private IEnumerable<NanoDevice<NanoNetworkDevice>> _networkNanoFrameworkDevices =>
            NanoFrameworkDevices.OfType<NanoDevice<NanoNetworkDevice>>();

        private readonly ConcurrentDictionary<string, CachedDeviceInfo> _devicesCache =
            new ConcurrentDictionary<string, CachedDeviceInfo>();

        /// <summary>
        /// Creates an Network debug client
        /// </summary>
        public PortTcpIpManager(bool startDeviceWatchers = true, int discoveryPort = DiscoveryPort)
        {
            _deviceWatcher = new DeviceWatcher(this, discoveryPort);

            // subscribe before anything can start the watcher, so no event is missed
            InitializeDeviceWatchers();

            if (startDeviceWatchers)
            {
                Task.Factory.StartNew(() =>
                {
                    lock (_autoStartLock)
                    {
                        if (!_autoStartCancelled)
                        {
                            StartNetworkDeviceWatchers();
                        }
                    }
                });
            }
        }

        /// <summary>
        /// Provides the token to authenticate with a device that requires it. The argument is the id of the device (<c>tcpip://host:port</c>).
        /// Returning <see langword="null"/> or an empty string means there is no token.
        /// </summary>
        public Func<string, string> TokenProvider { get; set; }

        // devices added by the user: they are kept even when they're not reachable, and are not removed by the device itself leaving
        private readonly Dictionary<string, NetworkDeviceInformation> _manualDevices =
            new Dictionary<string, NetworkDeviceInformation>();

        private readonly object _manualDevicesLock = new object();

        private bool IsManualDevice(string deviceId)
        {
            lock (_manualDevicesLock)
            {
                return _manualDevices.ContainsKey(deviceId);
            }
        }

        /// <summary>
        /// Checks the known devices again: the ones that don't answer are removed from the list, and the manually added
        /// devices that are not in the list are tried again. Devices in use are left alone.
        /// </summary>
        public override void ReScanDevices()
        {
            if (_disposed)
            {
                return;
            }

            // the enumeration restarts now, it completes when the devices are checked
            IsDevicesEnumerationComplete = false;

            Task.Run(() =>
            {
                try
                {
                    RemoveUnreachableDevices();

                    RetryManualDevices();
                }
                finally
                {
                    if (!_disposed)
                    {
                        ProcessDeviceEnumerationComplete();
                    }
                }
            });
        }

        private void RemoveUnreachableDevices()
        {
            List<NanoDevice<NanoNetworkDevice>> listedDevices;
            using (NanoFrameworkDevices.Lock())
            {
                listedDevices = _networkNanoFrameworkDevices.ToList();
            }

            foreach (var device in listedDevices)
            {
                if (_disposed)
                {
                    return;
                }

                if (!IsDeviceReachable(device)
                    && NetworkDeviceInformation.TryParse(device.DeviceId, out var info))
                {
                    RemoveDeviceFromList(info);
                }
            }
        }

        private void RetryManualDevices()
        {
            List<NetworkDeviceInformation> manualDevices;
            lock (_manualDevicesLock)
            {
                manualDevices = _manualDevices.Values.ToList();
            }

            foreach (var info in manualDevices)
            {
                if (_disposed)
                {
                    return;
                }

                // same exclusive access that the discovery and the manual add take
                using var access = GlobalExclusiveDeviceAccess.TryGet(info);

                if (access is null)
                {
                    // someone else is using it
                    continue;
                }

                // authentication failures are logged and not thrown
                AddDeviceToListAsync(info, false);
            }
        }

        private bool IsDeviceReachable(NanoDevice<NanoNetworkDevice> device)
        {
            if (device.DebugEngine?.IsConnected == true)
            {
                // in use
                return true;
            }

            using var access = GlobalExclusiveDeviceAccess.TryGet(device, NanoNetworkDevice.SafeDefaultTimeout);

            if (access is null)
            {
                // someone else is using it
                return true;
            }

            try
            {
                if (device.DebugEngine is null)
                {
                    device.CreateDebugEngine();
                }

                var reachable = device.DebugEngine.Connect(NanoNetworkDevice.SafeDefaultTimeout);

                ReleaseConnection(device);

                return reachable;
            }
            catch (DeviceAuthenticationFailedException)
            {
                ReleaseConnection(device);

                // it answers, it just needs the token
                return true;
            }
            catch
            {
                ReleaseConnection(device);

                return false;
            }
        }

        private static void ReleaseConnection(NanoDevice<NanoNetworkDevice> device)
        {
            try
            {
                device.DebugEngine?.Stop();
            }
            catch
            {
                // nothing else to do if the connection can't be released
            }
        }

        public override void StartDeviceWatchers()
        {
            ThrowIfDisposed();

            CancelAutoStart();

            if (!_watchersStarted)
            {
                StartDeviceWatchersInternal();
            }
        }

        public override void StopDeviceWatchers()
        {
            CancelAutoStart();

            StopDeviceWatchersInternal();
        }

        private void CancelAutoStart()
        {
            lock (_autoStartLock)
            {
                _autoStartCancelled = true;
            }
        }

        private void ThrowIfDisposed()
        {
#if NET7_0_OR_GREATER
            ObjectDisposedException.ThrowIf(_disposed, this);
#else
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PortTcpIpManager));
            }
#endif
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            lock (_autoStartLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;

                _autoStartCancelled = true;
            }

            if (disposing)
            {
                // dispose the watcher first: from now on it can't be started again,
                // so the wait below is guaranteed to see its thread exit
                _deviceWatcher.Dispose();

                // make sure the watcher has stopped and release all the network devices
                StopDeviceWatchersInternal();

                _deviceWatcher.Added -= OnDeviceAdded;
                _deviceWatcher.Removed -= OnDeviceRemoved;
            }

            base.Dispose(disposing);
        }

        #region Device watcher management and host app status handling

        /// <summary>
        /// Initialize device watchers. Must call here the initialization methods for all devices that we want to set watch.
        /// </summary>
        private void InitializeDeviceWatchers()
        {
            _deviceWatcher.Added += OnDeviceAdded;
            _deviceWatcher.Removed += OnDeviceRemoved;
        }

        public void StartNetworkDeviceWatchers()
        {
            ThrowIfDisposed();

            // Initialize the Network device watchers to be notified when devices are connected/removed
            StartDeviceWatchersInternal();
        }

        /// <summary>
        /// Starts all device watchers including ones that have been individually stopped.
        /// </summary>
        private void StartDeviceWatchersInternal()
        {
            if (_disposed)
            {
                return;
            }

            // Start all device watchers

            _deviceWatcher.Start();

            _watchersStarted = true;

            IsDevicesEnumerationComplete = false;

            // network devices announce themselves, there's no initial list to enumerate:
            // enumeration is complete as soon as the watcher is started, devices announcing later are added as they come
            ProcessDeviceEnumerationComplete();
        }

        /// <summary>
        /// Stops all device watchers.
        /// </summary>
        private void StopDeviceWatchersInternal()
        {
            _deviceWatcher.StopAndWait(Timeout.Infinite);

            List<string> devicesToRemove;
            using (NanoFrameworkDevices.Lock())
            {
                devicesToRemove = _networkNanoFrameworkDevices.Select(nanoDevice => nanoDevice.DeviceId).ToList();
            }

            foreach (var deviceId in devicesToRemove)
            {
                // get device...
                NanoDeviceBase device;
                using (NanoFrameworkDevices.Lock())
                {
                    var deviceEntry = FindDevice(deviceId);
                    if (deviceEntry is null)
                    {
                        // this is not a TcpIp-connected device and is managed by another PortManager
                        continue;
                    }

                    // ... and remove it from collection
                    _networkDevices.Remove(deviceEntry);

                    device = FindNanoFrameworkDevice(deviceId);
                    if (device is null)
                    {
                        continue;
                    }

                    // ... and remove it from collection
                    NanoFrameworkDevices.Remove(device);
                }
                device.DebugEngine?.StopProcessing();
                device.DebugEngine?.Stop(true);
            }

            _watchersStarted = false;
        }

        #endregion


        #region Methods to manage device list add, remove, etc
        /// <summary>
        /// Get the device that communicates via the network port, provided it has been added to the
        /// list of known devices.
        /// </summary>
        /// <param name="networkDevice">The name of the network device.</param>
        /// <returns></returns>
        public static NanoDeviceBase GetRegisteredDevice(NetworkDeviceInformation networkDevice)
        {
            if (networkDevice is not null)
            {
                var devices = NanoDeviceCollection.Instance;
                using (devices.Lock())
                {
                    return devices.FirstOrDefault(d => (d as NanoDevice<NanoNetworkDevice>)?.DeviceId == networkDevice.DeviceId);
                }
            }
            return null;
        }

        /// <summary>
        /// Creates a DeviceListEntry for a device and adds it to the list of devices
        /// </summary>
        /// <param name="networkDevice">The device to add.</param>
        /// <param name="throwOnAuthenticationFailure">Throw <see cref="DeviceAuthenticationFailedException"/> if the device needs a token that
        /// isn't available or is rejected. Discovered devices only log it.</param>
        private (NanoDeviceBase device, bool isNew) AddDeviceToListAsync(
            NetworkDeviceInformation networkDevice,
            bool throwOnAuthenticationFailure)
        {
            bool isNew = false;

            // search the device list for a device with a matching interface ID
            NetworkDeviceInformation networkMatch;
            NanoDeviceBase nanoFrameworkDeviceMatch;
            using (NanoFrameworkDevices.Lock())
            {
                networkMatch = FindDevice(networkDevice.DeviceId);

                // search the nanoFramework device list for a device with a matching interface ID
                nanoFrameworkDeviceMatch = FindNanoFrameworkDevice(networkDevice.DeviceId);
            }

            // Add the device if it's new
            if (networkMatch is null && nanoFrameworkDeviceMatch is null)
            {
                OnLogMessageAvailable(NanoDevicesEventSource.Log.CandidateDevice(networkDevice.DeviceId));

                // Create a new element for this device and...
                var newNanoFrameworkDevice = new NanoDevice<NanoNetworkDevice>();
                newNanoFrameworkDevice.DeviceId = networkDevice.DeviceId;
                newNanoFrameworkDevice.ConnectionPort = new PortTcpIp(this, newNanoFrameworkDevice, networkDevice);
                newNanoFrameworkDevice.TokenProvider = deviceId => TokenProvider?.Invoke(deviceId);

                var connectResult = newNanoFrameworkDevice.ConnectionPort.ConnectDevice();

                if (connectResult == ConnectPortResult.Unauthorized)
                {
                    OnLogMessageAvailable(
                        NanoDevicesEventSource.Log.UnauthorizedAccessToDevice(networkDevice.DeviceId));
                }
                else if (connectResult == ConnectPortResult.Connected)
                {
                    bool valid;

                    try
                    {
                        valid = CheckValidNanoFrameworkNetworkDevice(newNanoFrameworkDevice);
                    }
                    catch (DeviceAuthenticationFailedException ex)
                    {
                        HandleAuthenticationFailure(ex, newNanoFrameworkDevice);

                        if (throwOnAuthenticationFailure)
                        {
                            throw;
                        }

                        return (null, false);
                    }

                    if (valid
                        && NanoFrameworkDeviceAdd(newNanoFrameworkDevice, networkDevice))
                    {
                        OnLogMessageAvailable(
                            NanoDevicesEventSource.Log.ValidDevice($"{newNanoFrameworkDevice.Description}"));

                        nanoFrameworkDeviceMatch = newNanoFrameworkDevice;
                        isNew = true;
                    }
                    else
                    {
                        // disconnect
                        newNanoFrameworkDevice.Disconnect();
                    }
                }
                else
                {
                    OnLogMessageAvailable(NanoDevicesEventSource.Log.QuitDevice(networkDevice.DeviceId));
                }
            }

            return (nanoFrameworkDeviceMatch, isNew);
        }

        private void HandleAuthenticationFailure(
            DeviceAuthenticationFailedException ex,
            NanoDevice<NanoNetworkDevice> device)
        {
            OnLogMessageAvailable(
                NanoDevicesEventSource.Log.CriticalError($"ERROR: {ex.Message}"));

            device.Disconnect();
        }

        /// <summary>
        /// Adds a validated device to the collections.
        /// </summary>
        /// <returns><see langword="false"/> if the device was discarded because this manager has been disposed.
        /// The caller is responsible for disconnecting it.</returns>
        private bool NanoFrameworkDeviceAdd(
            NanoDevice<NanoNetworkDevice> newNanoFrameworkDevice,
            NetworkDeviceInformation networkDevice)
        {
            using (NanoFrameworkDevices.Lock())
            {
                // checked inside the lock: Dispose() removes the devices under this same lock,
                // so a validation that completes after that can't bring a device back into the list
                if (!_disposed)
                {
                    //add device to the collection
                    NanoFrameworkDevices.Add(newNanoFrameworkDevice);
                    _networkDevices.Add(networkDevice);

                    return true;
                }
            }

            // manager disposed while the device was being validated: release the debug engine
            newNanoFrameworkDevice.DebugEngine?.Dispose();
            newNanoFrameworkDevice.DebugEngine = null;

            return false;
        }

        public override void DisposeDevice(string instanceId)
        {
            var deviceToDispose =
                _networkNanoFrameworkDevices.FirstOrDefault(nanoDevice => nanoDevice.DeviceId == instanceId);

            Task.Run(() => { deviceToDispose?.Dispose(); });
        }

        private void RemoveDeviceFromList(NetworkDeviceInformation networkDevice)
        {
            // Removes the device entry from the internal list; therefore the UI
            OnLogMessageAvailable(NanoDevicesEventSource.Log.DeviceDeparture(networkDevice.DeviceId));

            // get device...
            NanoDeviceBase device;
            using (NanoFrameworkDevices.Lock())
            {
                var deviceEntry = FindDevice(networkDevice.DeviceId);
                if (deviceEntry != null)
                {
                    _networkDevices.Remove(deviceEntry);
                }

                device = FindNanoFrameworkDevice(networkDevice.DeviceId);
                if (device != null)
                {
                    // ... and remove it from collection
                    NanoFrameworkDevices.Remove(device);
                }
            }

            // get rid of debug engine, if that was created
            device?.DebugEngine?.StopProcessing();
            device?.DebugEngine?.Dispose();

            // disconnect device
            device?.Disconnect(true);
        }

        /// <summary>
        /// Searches through the existing list of devices for the first DeviceListEntry that has
        /// the specified device Id.
        /// </summary>
        private NetworkDeviceInformation FindDevice(string deviceId) =>
            _networkDevices.FirstOrDefault(d => d.DeviceId == deviceId);

        private NanoDeviceBase FindNanoFrameworkDevice(string deviceId) =>
            _networkNanoFrameworkDevices.FirstOrDefault(d => d.DeviceId == deviceId);

        /// <summary>
        /// Remove the device from the device list 
        /// </summary>
        private void OnDeviceRemoved(object sender, NetworkDeviceInformation networkDevice)
        {
            if (IsManualDevice(networkDevice.DeviceId))
            {
                // the user keeps it until removing it
                return;
            }

            RemoveDeviceFromList(networkDevice);
        }

        /// <summary>
        /// This function will add the device to the listOfDevices
        /// </summary>
        private void OnDeviceAdded(object sender, NetworkDeviceInformation networkDevice)
        {
            OnLogMessageAvailable(NanoDevicesEventSource.Log.DeviceArrival(networkDevice.DeviceId));

            AddDeviceToListAsync(networkDevice, false);
        }

        #endregion


        #region Handlers and events for Device Enumeration Complete

        private void ProcessDeviceEnumerationComplete()
        {
            int count;
            using (NanoFrameworkDevices.Lock())
            {
                IsDevicesEnumerationComplete = true;
                count = NanoFrameworkDevices.OfType<NanoDevice<NanoNetworkDevice>>().Count();
            }

            OnLogMessageAvailable(
                NanoDevicesEventSource.Log.NetworkDeviceEnumerationCompleted(count));

            // fire event that Network enumeration is complete 
            OnDeviceEnumerationCompleted();
        }

        private bool CheckValidNanoFrameworkNetworkDevice(
            NanoDevice<NanoNetworkDevice> device)
        {
            bool validDevice = false;

            // store device ID
            string deviceId = device.DeviceId;

            try
            {
                // sanity check for invalid or null device
                validDevice = ValidateDevice(device, deviceId);

                if (validDevice)
                {
                    // there should be a valid nanoFramework device at the other end

                    // store connection ID
                    device.ConnectionId = deviceId;

                    // store device in cache
                    var cachedDevice = new CachedDeviceInfo(
                        device.TargetName,
                        device.Platform);

                    _devicesCache.TryAdd(
                        deviceId,
                        cachedDevice);

                    // disconnect device
                    device.DebugEngine.Stop(true);
                }
                else
                {
                    // remove from cache
                    _devicesCache.TryRemove(deviceId, out var dummy);

                    device.DebugEngine?.Stop();
                    device.DebugEngine?.Dispose();
                    device.DebugEngine = null;
                    device.Device?.Dispose();
                }
            }
            catch (DeviceAuthenticationFailedException)
            {
                // the caller decides what to do about it, release the device
                _devicesCache.TryRemove(deviceId, out var dummy);

                try
                {
                    device.DebugEngine?.Stop();
                    device.DebugEngine?.Dispose();
                    device.DebugEngine = null;
                }
                catch
                {
                    // catch all trying to get rid of the device
                }

                throw;
            }
            catch (Exception /* ex */
            ) // we could eat simple programming errors here - like a bad cast or other problem when changing code
            {
                // "catch all" required because the device open & check calls might fail for a number of reasons
                // if there is a deviceID, remove it from cache, just in case
                if (deviceId != null)
                {
                    _devicesCache.TryRemove(deviceId, out var dummy);
                }

                try
                {
                    device.DebugEngine?.Stop();
                    device.DebugEngine?.Dispose();
                    device.DebugEngine = null;
                    device.Device?.Dispose();
                }
                catch
                {
                    // catch all trying to get rid of the device
                }
            }

            return validDevice;
        }

        private bool ValidateDevice(NanoDevice<NanoNetworkDevice> device, string deviceId)
        {
            bool validDevice = false;
            if (device.Device != null)
            {
                // check if this device is on cache
                var isKnownDevice = _devicesCache.TryGetValue(deviceId, out var cachedDevice);

                OnLogMessageAvailable(NanoDevicesEventSource.Log.CheckingValidDevice($" {deviceId}"));

                if (device.DebugEngine == null)
                {
                    device.CreateDebugEngine();
                }

                // try to "just" connect to the device meaning...
                // ... don't request capabilities or force anything except the absolute minimum required, plus...
                // ... it's OK to use a very short timeout as we'll be exchanging really short packets with the device
                if (!device.DebugEngine.Connect(NanoNetworkDevice.SafeDefaultTimeout))
                {
                    return false;
                }

                if (isKnownDevice)
                {
                    // skip getting properties from device
                    device.TargetName = cachedDevice.TargetName;
                    device.Platform = cachedDevice.PlatformName;

                    return true;
                }

                if (device.DebugEngine.IsConnectedTonanoBooter)
                {
                    TryGetMonitorTargetInfo(device);
                }
                else
                {
                    TryGetTargetInfo(device);
                }

                if (string.IsNullOrEmpty(device.TargetName)
                    || string.IsNullOrEmpty(device.Platform))
                {
                    OnLogMessageAvailable(
                        NanoDevicesEventSource.Log.CriticalError(
                            $"ERROR: {device.DeviceId} failed to get target information"));

                    validDevice = false;
                }
                else
                {
                    validDevice = true;
                }
            }

            return validDevice;
        }

        // retry settings for getting the target details
        private const int TargetInfoRetryCount = 2;

        private static TimeSpan TargetInfoRetryDelay(int retry) => TimeSpan.FromMilliseconds((retry + 1) * 200);

        private static bool IsNullReference(Exception ex) => ex is NullReferenceException;

        private static void TryGetTargetInfo(NanoDevice<NanoNetworkDevice> device)
        {
            var deviceInfo = RetryHelper.Execute(
                () =>
                {
                    if (device.DebugEngine != null)
                    {
                        return device.DebugEngine.GetTargetInfo();
                    }
                    else
                    {
                        return new CLRCapabilities.TargetInfoProperties();
                    }
                },
                TargetInfoRetryCount,
                TargetInfoRetryDelay,
                shouldRetryOnException: IsNullReference,
                shouldRetryOnResult: r => r.TargetName == null);

            if (!string.IsNullOrEmpty(deviceInfo.TargetName))
            {
                device.TargetName = deviceInfo.TargetName;
                device.Platform = deviceInfo.Platform;
            }
        }

        private void TryGetMonitorTargetInfo(NanoDevice<NanoNetworkDevice> device)
        {
            // try first with new command
            var targetInfo = RetryHelper.Execute(
                () => device.DebugEngine.GetMonitorTargetInfo(),
                TargetInfoRetryCount,
                TargetInfoRetryDelay,
                shouldRetryOnException: IsNullReference,
                // no reply (null) is retried too, then falls back to the deprecated command below
                shouldRetryOnResult: r => r?.TargetName == null);

            if (targetInfo != null)
            {
                device.TargetName = targetInfo.TargetName;
                device.Platform = targetInfo.PlatformName;
            }
            else
            {
                // try again with deprecated command
                var deviceInfo = RetryHelper.Execute(
                    () => device.DebugEngine.GetMonitorOemInfo(),
                    TargetInfoRetryCount,
                    TargetInfoRetryDelay,
                    shouldRetryOnException: IsNullReference,
                    shouldRetryOnResult: r => r == null);

                if (deviceInfo != null)
                {
                    device.TargetName = deviceInfo.TargetName;
                    device.Platform = deviceInfo.PlatformName;
                }
            }
        }

        protected virtual void OnDeviceEnumerationCompleted()
        {
            DeviceEnumerationCompleted?.Invoke(this, new EventArgs());
        }

        internal void OnLogMessageAvailable(string message)
        {
            LogMessageAvailable?.Invoke(this, new StringEventArgs(message));
        }

        /// <inheritdoc/>
        public override NanoDeviceBase AddDevice(string deviceId)
        {
            ThrowIfDisposed();

            // expected format is "tcpip://{Host}:{Port}", the host is an IPv4 address or a host name
            if (!NetworkDeviceInformation.TryParse(deviceId, out var info))
            {
                throw new ArgumentException("Invalid tcpip format.", nameof(deviceId));
            }

            return AddDevice(info.Host, info.Port);
        }

        /// <summary>
        /// Adds a device by its address, without it having to announce itself. The device is kept in the list of devices
        /// until <see cref="RemoveDevice"/> is called: if it isn't reachable now, it is tried again at <see cref="ReScanDevices"/>.
        /// </summary>
        /// <param name="host">IPv4 address or host name of the device.</param>
        /// <param name="port">TCP port of the device.</param>
        /// <returns>The device, or <see langword="null"/> if it isn't reachable.</returns>
        /// <exception cref="ArgumentException">The host or port are not valid.</exception>
        /// <exception cref="DeviceAuthenticationFailedException">The device requires a token that <see cref="TokenProvider"/> doesn't provide or the device rejects.</exception>
        public NanoDeviceBase AddDevice(string host, int port)
        {
            ThrowIfDisposed();

            if (!NetworkDeviceInformation.TryParse($"{host}:{port}", out var info))
            {
                throw new ArgumentException($"Invalid host or port: {host}:{port}.");
            }

            lock (_manualDevicesLock)
            {
                _manualDevices[info.DeviceId] = info;
            }

            // same exclusive access that the discovery takes
            using var access = GlobalExclusiveDeviceAccess.TryGet(info);

            return AddDeviceToListAsync(info, true).device;
        }

        /// <summary>
        /// Removes a device from the list of devices, and disconnects it. It also stops a device added with <see cref="AddDevice(string, int)"/>
        /// from being kept, but a device that announces itself is added again.
        /// </summary>
        /// <param name="deviceId">The id of the device, <c>tcpip://host:port</c>.</param>
        public void RemoveDevice(string deviceId)
        {
            if (!NetworkDeviceInformation.TryParse(deviceId, out var info))
            {
                throw new ArgumentException("Invalid tcpip format.", nameof(deviceId));
            }

            lock (_manualDevicesLock)
            {
                _manualDevices.Remove(info.DeviceId);
            }

            RemoveDeviceFromList(info);
        }

        /// <summary>
        /// Event that is raised when enumeration of all watched devices is complete.
        /// </summary>
        public override event EventHandler DeviceEnumerationCompleted;
        public override event EventHandler<StringEventArgs> LogMessageAvailable;

        #endregion
    }
}
