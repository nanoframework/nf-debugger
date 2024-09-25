// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using nanoFramework.Tools.Debugger.WireProtocol;
using Polly;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    public partial class PortSerialManager : PortBase
    {
        private readonly DeviceWatcher _deviceWatcher;

        // Serial device watchers started flag
        private bool _watchersStarted = false;

        // counter of device watchers completed
        private int _deviceWatchersCompletedCount = 0;

        private readonly Random _delay = new Random(DateTime.Now.Millisecond);

        private readonly ConcurrentDictionary<string, CachedDeviceInfo> _devicesCache = new ConcurrentDictionary<string, CachedDeviceInfo>();

        public int BootTime { get; set; }

        /// <summary>
        /// Creates an Serial debug client
        /// </summary>
        /// <param name="startDeviceWatchers">Indicates whether to start the device watcher.</param>
        /// <param name="portExclusionList">The collection of serial ports to ignore when searching for devices.
        /// Changes in the collection after the start of the device watcher are taken into account.</param>
        /// <param name="bootTime"></param>
        public PortSerialManager(bool startDeviceWatchers = true, List<string> portExclusionList = null, int bootTime = 3000)
        {
            _deviceWatcher = new(this);

            BootTime = bootTime;

            if (portExclusionList != null)
            {
                PortExclusionList = portExclusionList;
            }

            Task.Factory.StartNew(() =>
            {

                InitializeDeviceWatchers();

                if (startDeviceWatchers)
                {
                    StartSerialDeviceWatchers();
                }
            });
        }

        public override void ReScanDevices()
        {
            // need to reset this here to have intimidate effect
            IsDevicesEnumerationComplete = false;

            Task.Run(delegate
            {
                StopDeviceWatchersInternal();

                StartDeviceWatchersInternal();
            });
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
            _deviceWatcher.AllNewDevicesAdded += ProcessDeviceEnumerationComplete;
        }

        public void StartSerialDeviceWatchers()
        {
            // Initialize the Serial device watchers to be notified when devices are connected/removed
            StartDeviceWatchersInternal();
        }

        /// <summary>
        /// Starts all device watchers including ones that have been individually stopped.
        /// </summary>
        private void StartDeviceWatchersInternal()
        {
            // Start all device watchers

            _deviceWatcher.Start(PortExclusionList);

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

            NanoFrameworkDevicesRemoveAllSerial();

            _watchersStarted = false;
        }

        private void NanoFrameworkDevicesRemoveAllSerial()
        {
            List<string> devicesToRemove;

            // also clear nanoFramework devices list
            lock (NanoFrameworkDevices)
            {
                devicesToRemove = NanoFrameworkDevices.Select(nanoDevice => ((NanoDevice<NanoSerialDevice>)nanoDevice).DeviceId).ToList();
            }

            foreach (var deviceId in devicesToRemove)
            {
                RemoveDeviceFromList(deviceId);
            }
        }

        #endregion

        #region Methods to manage device list add, remove, etc

        /// <summary>
        /// Get the device that communicates via the serial port, provided it has been added to the
        /// list of known devices.
        /// </summary>
        /// <param name="portName">The port name of the device to get.</param>
        /// <returns>The <see cref="NanoDeviceBase"/> that communicates via the serial port, or <see langword="null"/> if the device is not found.</returns>
        public static NanoDeviceBase GetRegisteredDevice(string portName)
        {
            if (!string.IsNullOrWhiteSpace(portName))
            {
                var devices = NanoFrameworkDevices.Instance;

                lock (devices)
                {
                    return devices.FirstOrDefault(d => (d as NanoDevice<NanoSerialDevice>)?.DeviceId == portName);
                }
            }

            return null;
        }

        /// <summary>
        /// Adds a new <see cref="PortSerial"/> device to list of <see cref="NanoFrameworkDevices"/>.
        /// </summary>
        /// <param name="deviceId">The serial port name where the device is connected.</param>
        public override void AddDevice(string deviceId)
        {
            AddDeviceToListAsync(deviceId);
        }

        /// <summary>
        /// Adds a new <see cref="PortSerial"/> device to list of NanoFrameworkDevices.
        /// </summary>
        /// <param name="deviceId">The serial port name where the device is connected.</param>
        /// <returns>The device with the unique ID that is added or (if it was already discovered before) retrieved
        /// from the list of devices. Returns <see langword="null"/> if no device has been added.</returns>
        public override NanoDeviceBase AddAndReturnDevice(string deviceId)
        {
            return AddDeviceToListAsync(deviceId);
        }

        /// <summary>
        /// Creates a <see cref="NanoDevice{NanoSerialDevice}"/> and adds it to the list of devices.
        /// </summary>
        /// <param name="deviceId">The AQS used to find this device</param>
        private NanoDeviceBase AddDeviceToListAsync(string deviceId)
        {
            // search the nanoFramework device list for a device with a matching interface ID
            var nanoFrameworkDeviceMatch = FindNanoFrameworkDevice(deviceId);

            // Add the device if it's new
            if (nanoFrameworkDeviceMatch is null)
            {
                OnLogMessageAvailable(NanoDevicesEventSource.Log.CandidateDevice(deviceId));

                // Create a new element for this device and...
                var newNanoFrameworkDevice = new NanoDevice<NanoSerialDevice>();
                newNanoFrameworkDevice.DeviceId = deviceId;
                newNanoFrameworkDevice.ConnectionPort = new PortSerial(this, newNanoFrameworkDevice);
                newNanoFrameworkDevice.Transport = TransportType.Serial;

                var connectResult = newNanoFrameworkDevice.ConnectionPort.ConnectDevice();

                if (connectResult == ConnectPortResult.Unauthorized)
                {
                    OnLogMessageAvailable(NanoDevicesEventSource.Log.UnauthorizedAccessToDevice(deviceId));
                }
                else if (connectResult == ConnectPortResult.Connected)
                {
                    if (CheckValidNanoFrameworkSerialDevice(newNanoFrameworkDevice))
                    {
                        //add device to the collection
                        NanoFrameworkDeviceAdd(newNanoFrameworkDevice);

                        OnLogMessageAvailable(NanoDevicesEventSource.Log.ValidDevice($"{newNanoFrameworkDevice.Description}"));
                        nanoFrameworkDeviceMatch = newNanoFrameworkDevice;
                    }
                    else
                    {
                        // disconnect
                        newNanoFrameworkDevice.Disconnect();

                        // devices powered by the USB cable and that feature a serial converter (like an FTDI chip) 
                        // are still booting when the USB enumeration event raises
                        // so need to give them enough time for the boot sequence to complete before trying to communicate with them

                        // Failing to connect to debugger engine on first attempt occurs frequently on dual USB devices like ESP32 WROVER KIT.
                        // Seems to be something related with both devices using the same USB endpoint
                        // Another reason is that an ESP32 takes around 3 seconds to complete the boot sequence and launch the CLR.
                        // Until then the device will look non responsive or invalid to the detection mechanism that we're using.
                        // A nice workaround for this seems to be adding an extra random wait so the comms are not simultaneous.

                        int delay;
                        lock (_delay)
                        {
                            delay = _delay.Next(200, 600);
                        }

                        Thread.Sleep(BootTime + delay);

                        OnLogMessageAvailable(NanoDevicesEventSource.Log.CheckingValidDevice($" {newNanoFrameworkDevice.DeviceId} *** 2nd attempt ***"));

                        connectResult = newNanoFrameworkDevice.ConnectionPort.ConnectDevice();

                        if (connectResult == ConnectPortResult.Unauthorized)
                        {
                            OnLogMessageAvailable(NanoDevicesEventSource.Log.UnauthorizedAccessToDevice(deviceId));
                        }
                        else if (connectResult == ConnectPortResult.Connected)
                        {
                            if (CheckValidNanoFrameworkSerialDevice(newNanoFrameworkDevice, true))
                            {
                                NanoFrameworkDeviceAdd(newNanoFrameworkDevice);

                                OnLogMessageAvailable(NanoDevicesEventSource.Log.ValidDevice($"{newNanoFrameworkDevice.Description}"));
                            }
                            else
                            {
                                OnLogMessageAvailable(NanoDevicesEventSource.Log.QuitDevice(deviceId));
                            }
                        }
                        else
                        {
                            OnLogMessageAvailable(NanoDevicesEventSource.Log.QuitDevice(deviceId));
                        }
                    }
                }
                else
                {
                    OnLogMessageAvailable(NanoDevicesEventSource.Log.QuitDevice(deviceId));
                }
            }
            return nanoFrameworkDeviceMatch;
        }

        /// <summary>
        /// Adds a device to the collection (if new).
        /// </summary>
        /// <param name="newNanoFrameworkDevice">The new <see cref="NanoSerialDevice"/></param>
        private void NanoFrameworkDeviceAdd(NanoDevice<NanoSerialDevice> newNanoFrameworkDevice)
        {
            lock (NanoFrameworkDevices)
            {
                if (newNanoFrameworkDevice != null && NanoFrameworkDevices.OfType<NanoDevice<NanoSerialDevice>>().Count(i => i.DeviceId == newNanoFrameworkDevice.DeviceId) == 0)
                {
                    //add device to the collection
                    NanoFrameworkDevices.Add(newNanoFrameworkDevice);
                }
            }
        }

        public override void DisposeDevice(string instanceId)
        {
            NanoDeviceBase deviceToDispose;
            lock (NanoFrameworkDevices)
            {
                deviceToDispose = NanoFrameworkDevices.FirstOrDefault(nanoDevice => ((NanoDevice<NanoSerialDevice>)nanoDevice).DeviceId == instanceId);
            }

            if (deviceToDispose != null)
            {
                Task.Run(() =>
                {
                    ((NanoDevice<NanoSerialDevice>)deviceToDispose).Dispose();
                });
            }
        }

        private void RemoveDeviceFromList(string deviceId)
        {
            OnLogMessageAvailable(NanoDevicesEventSource.Log.DeviceDeparture(deviceId));

            List<NanoDevice<NanoSerialDevice>> devices;
            lock (NanoFrameworkDevices)
            {
                // get devices
                devices = NanoFrameworkDevices.OfType<NanoDevice<NanoSerialDevice>>()
                    .Where(i => i.DeviceId == deviceId).ToList();
            }

            // remove them from collection
            devices.ForEach(RemoveNanoFrameworkDevices);
        }

        private void RemoveNanoFrameworkDevices(NanoDevice<NanoSerialDevice> device)
        {
            if (device is null)
            {
                return;
            }
            lock (NanoFrameworkDevices)
            {
                NanoFrameworkDevices.Remove(device);
            }
            device?.DebugEngine?.StopProcessing();
            device?.DebugEngine?.Dispose();
        }

        private NanoDeviceBase FindNanoFrameworkDevice(string deviceId)
        {
            if (deviceId != null)
            {
                lock (NanoFrameworkDevices.Instance)
                {
                    return NanoFrameworkDevices.FirstOrDefault(d => (d as NanoDevice<NanoSerialDevice>)?.DeviceId == deviceId);
                }
            }

            return null;
        }

        /// <summary>
        /// Remove the device from the device list 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="serialPort"></param>
        private void OnDeviceRemoved(object sender, string serialPort)
        {
            RemoveDeviceFromList(serialPort);
        }

        // TODO
        /// <summary>
        /// This function will add the device to the listOfDevices
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="serialPort"></param>
        private void OnDeviceAdded(object sender, string serialPort)
        {
            // check against exclusion list
            bool exclude;
            lock (PortExclusionList)
            {
                exclude = PortExclusionList.Contains(serialPort);
            }
            if (exclude)
            {
                OnLogMessageAvailable(NanoDevicesEventSource.Log.DroppingDeviceToExclude(serialPort));
                return;
            }

            OnLogMessageAvailable(NanoDevicesEventSource.Log.DeviceArrival(serialPort));

            Policy.Handle<InvalidOperationException>()
                .WaitAndRetry(10, retryCount => TimeSpan.FromMilliseconds((retryCount * retryCount) * 25),
                    onRetry: (exception, delay, retryCount, context) => LogRetry(exception, delay, retryCount, context))
                .Execute(() => AddDeviceToListAsync(serialPort));
        }

        private void LogRetry(Exception exception, TimeSpan delay, object retryCount, object context)
        {
            string logMsg = $"Error in AddDeviceToListAsync: {exception.Message} retryCount: {retryCount}, delay msec: {delay.TotalMilliseconds}";

            Debug.WriteLine(logMsg);
            OnLogMessageAvailable(NanoDevicesEventSource.Log.CriticalError(logMsg));
        }

        #endregion


        #region Handlers and events for Device Enumeration Complete 

        private void ProcessDeviceEnumerationComplete(object sender)
        {
            int count;
            lock (NanoFrameworkDevices)
            {
                if (IsDevicesEnumerationComplete)
                {
                    // Nothing has changed
                    return;
                }
                // all watchers have completed enumeration
                IsDevicesEnumerationComplete = true;

                count = NanoFrameworkDevices.OfType<NanoDevice<NanoSerialDevice>>().Count();
            }
            OnLogMessageAvailable(NanoDevicesEventSource.Log.SerialDeviceEnumerationCompleted(count));

            // fire event that Serial enumeration is complete 
            OnDeviceEnumerationCompleted();
        }

        private bool CheckValidNanoFrameworkSerialDevice(
            NanoDevice<NanoSerialDevice> device,
            bool longDelay = false)
        {
            bool validDevice = false;
            bool isKnownDevice = false;

            // store device ID
            string deviceId = device.DeviceId;

            try
            {
                // sanity check for invalid or null device base
                if ((SerialPort)device.DeviceBase != null)
                {
                    // check if this device is on cache
                    isKnownDevice = _devicesCache.TryGetValue(deviceId, out var cachedDevice);

                    // need to go through all the valid baud rates: 921600, 460800 and 115200.
                    foreach (int baudRate in PortSerial.ValidBaudRates)
                    {
                        if (device.DebugEngine == null)
                        {
                            device.CreateDebugEngine();
                        }

                        if (isKnownDevice)
                        {
                            // OK to go with stored cache
                            ((SerialPort)device.DeviceBase).BaudRate = cachedDevice.BaudRate;
                        }
                        else
                        {
                            ((SerialPort)device.DeviceBase).BaudRate = baudRate;
                        }

                        // better flush the UART FIFOs
                        ((SerialPort)device.DeviceBase).DiscardInBuffer();
                        ((SerialPort)device.DeviceBase).DiscardOutBuffer();

                        OnLogMessageAvailable(NanoDevicesEventSource.Log.CheckingValidDevice($" {deviceId} @ {baudRate}"));

                        // try to "just" connect to the device meaning...
                        // ... don't request capabilities or force anything except the absolute minimum required, plus...
                        // ... it's OK to use a very short timeout as we'll be exchanging really short packets with the device
                        if (device.DebugEngine.Connect(
                            longDelay ? 2 * NanoSerialDevice.SafeDefaultTimeout : NanoSerialDevice.SafeDefaultTimeout))
                        {
                            if (isKnownDevice)
                            {
                                // skip getting properties from device
                                device.TargetName = cachedDevice.TargetName;
                                device.Platform = cachedDevice.PlatformName;

                                validDevice = true;
                                break;
                            }

                            // set retry policies
                            var targetInfoPropertiesPolicy = Policy.Handle<NullReferenceException>().OrResult<CLRCapabilities.TargetInfoProperties>(r => r.TargetName == null)
                                                        .WaitAndRetry(2, retryAttempt => TimeSpan.FromMilliseconds((retryAttempt + 1) * 200));
                            var targetInfoPolicy = Policy.Handle<NullReferenceException>().OrResult<TargetInfo>(r => r.TargetName == null)
                                                            .WaitAndRetry(2, retryAttempt => TimeSpan.FromMilliseconds((retryAttempt + 1) * 200));
                            var targetReleaseInfoPolicy = Policy.Handle<NullReferenceException>().OrResult<ReleaseInfo>(r => r == null)
                                                            .WaitAndRetry(2, retryAttempt => TimeSpan.FromMilliseconds((retryAttempt + 1) * 200));

                            if (device.DebugEngine.IsConnectedTonanoBooter)
                            {
                                // try first with new command
                                var targetInfo = targetInfoPolicy.Execute(() => device.DebugEngine.GetMonitorTargetInfo());

                                if (targetInfo != null)
                                {
                                    device.TargetName = targetInfo.TargetName;
                                    device.Platform = targetInfo.PlatformName;
                                }
                                else
                                {
                                    // try again with deprecated command
                                    var deviceInfo = targetReleaseInfoPolicy.Execute(() => device.DebugEngine.GetMonitorOemInfo());

                                    if (deviceInfo != null)
                                    {
                                        device.TargetName = deviceInfo.TargetName;
                                        device.Platform = deviceInfo.PlatformName;
                                    }
                                }
                            }
                            else
                            {
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

                            if (string.IsNullOrEmpty(device.TargetName)
                                || string.IsNullOrEmpty(device.Platform))
                            {
                                OnLogMessageAvailable(NanoDevicesEventSource.Log.CriticalError($"ERROR: {device.DeviceId} failed to get target information"));

                                validDevice = false;
                                break;
                            }
                            else
                            {
                                validDevice = true;
                                break;
                            }
                        }
                    }
                }

                if (validDevice)
                {
                    // there should be a valid nanoFramework device at the other end

                    device.SerialNumber = GetSerialNumber(deviceId);

                    // store valid baud rate from device detection
                    ((PortSerial)device.ConnectionPort).BaudRate = ((SerialPort)device.DeviceBase).BaudRate;

                    // store connection ID
                    device.ConnectionId = deviceId;

                    // store device in cache
                    var cachedDevice = new CachedDeviceInfo(
                        device.TargetName,
                        device.Platform,
                        ((SerialPort)device.DeviceBase).BaudRate);

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

                    if (device.DeviceBase != null)
                    {
                        ((SerialPort)device.DeviceBase).Close();
                        ((SerialPort)device.DeviceBase).Dispose();

                        device.DeviceBase = null;
                    }
                }
            }
            catch (Exception /* ex */)   // we could eat simple programming errors here - like a bad cast or other problem when changing code
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

                    if (device.DeviceBase != null)
                    {
                        ((SerialPort)device.DeviceBase).Close();
                        ((SerialPort)device.DeviceBase).Dispose();

                        device.DeviceBase = null;
                    }
                }
                catch
                {
                    // catch all trying to get rid of the device
                }
            }

            return validDevice;
        }

        protected virtual void OnDeviceEnumerationCompleted()
        {
            DeviceEnumerationCompleted?.Invoke(this, new EventArgs());
        }

        /// <summary>
        /// Event that is raised when enumeration of all watched devices is complete.
        /// </summary>
        public override event EventHandler DeviceEnumerationCompleted;
        public override event EventHandler<StringEventArgs> LogMessageAvailable;

        #endregion

        public static string GetSerialNumber(string value)
        {
            int startIndex = value.IndexOf("USB");

            int endIndex = value.LastIndexOf("#");

            // sanity check
            if (startIndex < 0 || endIndex < 0)
            {
                return null;
            }

            // get device ID portion
            var deviceIDCollection = value.Substring(startIndex, endIndex - startIndex).Split('#');

            return deviceIDCollection?.GetValue(2) as string;
        }

        internal void OnLogMessageAvailable(string message)
        {
            LogMessageAvailable?.Invoke(this, new StringEventArgs(message));
        }
    }
}
