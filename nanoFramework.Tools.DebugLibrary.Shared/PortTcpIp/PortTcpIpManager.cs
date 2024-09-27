// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger.WireProtocol;
using Polly;

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

        /// <summary>
        /// Internal list with the actual nF Network devices.
        /// This must be a static list as NanoFrameworkDevices is also global.
        /// Take care that all items of _networkDevices are in the NanoFrameworkDevices,
        /// and that there are no devices in NanoFrameworkDevices that should be present
        /// in _networkDevices but are not (and use NanoFrameworkDevices for locks).
        /// </summary>
        private static readonly List<NetworkDeviceInformation> _networkDevices = new List<NetworkDeviceInformation>();

        private IEnumerable<NanoDevice<NanoNetworkDevice>> _networkNanoFrameworkDevices =>
            NanoFrameworkDevices.Cast<NanoDevice<NanoNetworkDevice>>();

        private readonly ConcurrentDictionary<string, CachedDeviceInfo> _devicesCache =
            new ConcurrentDictionary<string, CachedDeviceInfo>();

        /// <summary>
        /// Creates an Network debug client
        /// </summary>
        public PortTcpIpManager(bool startDeviceWatchers = true, int discoveryPort = DiscoveryPort)
        {
            _deviceWatcher = new DeviceWatcher(this, discoveryPort);

            Task.Factory.StartNew(() =>
            {
                InitializeDeviceWatchers();

                if (startDeviceWatchers)
                {
                    StartNetworkDeviceWatchers();
                }
            });
        }

        public override void ReScanDevices()
        {
            //No active rescan logic for network listener manager
        }

        public override void StartDeviceWatchers()
        {
            if (!_watchersStarted)
            {
                StartDeviceWatchersInternal();
            }
        }

        public override void StopDeviceWatchers()
        {
            StopDeviceWatchersInternal();
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
            // Initialize the Network device watchers to be notified when devices are connected/removed
            StartDeviceWatchersInternal();
        }

        /// <summary>
        /// Starts all device watchers including ones that have been individually stopped.
        /// </summary>
        private void StartDeviceWatchersInternal()
        {
            // Start all device watchers

            _deviceWatcher.Start();

            _watchersStarted = true;

            IsDevicesEnumerationComplete = false;
        }

        /// <summary>
        /// Stops all device watchers.
        /// </summary>
        private void StopDeviceWatchersInternal()
        {
            if (_deviceWatcher.Status == DeviceWatcherStatus.Started)
            {
                _deviceWatcher.Stop();

                while (_deviceWatcher.Status != DeviceWatcherStatus.Stopped)
                {
                    Thread.Sleep(100);
                }
            }

            // Clear the list of devices so we don't have potentially disconnected devices around
            // also clear nanoFramework devices list
            List<string> devicesToRemove;
            lock (NanoFrameworkDevices)
            {
                devicesToRemove = _networkNanoFrameworkDevices.Select(nanoDevice => nanoDevice.DeviceId).ToList();
            }

            foreach (var deviceId in devicesToRemove)
            {
                // get device...
                NanoDeviceBase device;
                lock (NanoFrameworkDevices)
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
                var devices = NanoFrameworkDevices.Instance;
                lock (devices)
                {
                    return devices.FirstOrDefault(d => (d as NanoDevice<NanoNetworkDevice>)?.DeviceId == networkDevice.DeviceId);
                }
            }
            return null;
        }

        /// <summary>
        /// Creates a DeviceListEntry for a device and adds it to the list of devices
        /// </summary>
        private (NanoDeviceBase device, bool isNew) AddDeviceToListAsync(NetworkDeviceInformation networkDevice)
        {
            bool isNew = false;

            // search the device list for a device with a matching interface ID
            NetworkDeviceInformation networkMatch;
            NanoDeviceBase nanoFrameworkDeviceMatch;
            lock (NanoFrameworkDevices)
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
                newNanoFrameworkDevice.Transport = TransportType.TcpIp;

                var connectResult = newNanoFrameworkDevice.ConnectionPort.ConnectDevice();

                if (connectResult == ConnectPortResult.Unauthorized)
                {
                    OnLogMessageAvailable(
                        NanoDevicesEventSource.Log.UnauthorizedAccessToDevice(networkDevice.DeviceId));
                }
                else if (connectResult == ConnectPortResult.Connected)
                {
                    if (CheckValidNanoFrameworkNetworkDevice(newNanoFrameworkDevice))
                    {
                        lock (NanoFrameworkDevices)
                        {
                            //add device to the collection
                            NanoFrameworkDevices.Add(newNanoFrameworkDevice);
                            _networkDevices.Add(networkDevice);
                        }

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
            lock (NanoFrameworkDevices)
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
            RemoveDeviceFromList(networkDevice);
        }

        /// <summary>
        /// This function will add the device to the listOfDevices
        /// </summary>
        private void OnDeviceAdded(object sender, NetworkDeviceInformation networkDevice)
        {
            OnLogMessageAvailable(NanoDevicesEventSource.Log.DeviceArrival(networkDevice.DeviceId));

            var (_, isNew) = AddDeviceToListAsync(networkDevice);

            if (isNew && !IsDevicesEnumerationComplete)
            {
                ProcessDeviceEnumerationComplete();
            }
        }

        #endregion


        #region Handlers and events for Device Enumeration Complete

        private void ProcessDeviceEnumerationComplete()
        {
            int count;
            lock (NanoFrameworkDevices)
            {
                IsDevicesEnumerationComplete = true;
                count = NanoFrameworkDevices.OfType<NanoDevice<NanoNetworkDevice>>().Count();
            }

            // TODO: count are not serial devices
            OnLogMessageAvailable(
                NanoDevicesEventSource.Log.SerialDeviceEnumerationCompleted(count));

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

        private static void TryGetTargetInfo(NanoDevice<NanoNetworkDevice> device)
        {
            // set retry policies
            var targetInfoPropertiesPolicy = Policy.Handle<NullReferenceException>()
                .OrResult<CLRCapabilities.TargetInfoProperties>(r => r.TargetName == null)
                .WaitAndRetry(2,
                    retryAttempt => TimeSpan.FromMilliseconds((retryAttempt + 1) * 200));

            var deviceInfo = targetInfoPropertiesPolicy.Execute(() =>
            {
                if (device.DebugEngine != null)
                {
                    return device.DebugEngine.GetTargetInfo();
                }
                else
                {
                    return new CLRCapabilities.TargetInfoProperties();
                }
            });

            if (!string.IsNullOrEmpty(deviceInfo.TargetName))
            {
                device.TargetName = deviceInfo.TargetName;
                device.Platform = deviceInfo.Platform;
            }
        }

        private void TryGetMonitorTargetInfo(NanoDevice<NanoNetworkDevice> device)
        {
            // set retry policies
            var targetInfoPolicy = Policy.Handle<NullReferenceException>()
                .OrResult<TargetInfo>(r => r.TargetName == null)
                .WaitAndRetry(2,
                    retryAttempt => TimeSpan.FromMilliseconds((retryAttempt + 1) * 200));
            var targetReleaseInfoPolicy = Policy.Handle<NullReferenceException>()
                .OrResult<ReleaseInfo>(r => r == null)
                .WaitAndRetry(2,
                    retryAttempt => TimeSpan.FromMilliseconds((retryAttempt + 1) * 200));

            // try first with new command
            var targetInfo =
                targetInfoPolicy.Execute(() => device.DebugEngine.GetMonitorTargetInfo());

            if (targetInfo != null)
            {
                device.TargetName = targetInfo.TargetName;
                device.Platform = targetInfo.PlatformName;
            }
            else
            {
                // try again with deprecated command
                var deviceInfo = targetReleaseInfoPolicy.Execute(() =>
                    device.DebugEngine.GetMonitorOemInfo());

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
            // expected format is "tcpip://{Host}:{Port}"

            var match = Regex.Match(deviceId, $"\"(tcpip:\\/\\/)(?<host>\\d{{1,3}}\\.\\d{{1,3}}\\.\\d{{1,3}}\\.\\d{{1,3}}):(?<port>[0-9]{{1,5}})\"gm");
            if (!match.Success)
            {
                throw new ArgumentException("Invalid tcpip format.");
            }

            return AddDeviceToListAsync(new NetworkDeviceInformation(
                match.Groups["host"].Value,
                int.Parse(match.Groups["port"].Value))).device;
        }

        /// <summary>
        /// Event that is raised when enumeration of all watched devices is complete.
        /// </summary>
        public override event EventHandler DeviceEnumerationCompleted;
        public override event EventHandler<StringEventArgs> LogMessageAvailable;

        #endregion
    }
}
