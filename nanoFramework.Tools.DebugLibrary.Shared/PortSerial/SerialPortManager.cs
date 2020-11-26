﻿//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.Serial;
using nanoFramework.Tools.Debugger.WireProtocol;
using Polly;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Foundation;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    public partial class SerialPortManager : PortBase
    {
        // dictionary with mapping between Serial device watcher and the device ID
        private readonly Dictionary<DeviceWatcher, string> _mapDeviceWatchersToDeviceSelector;

        // Serial device watchers suspended flag
        private bool _watchersSuspended = false;

        // Serial device watchers started flag
        private bool _watchersStarted = false;

        // counter of device watchers completed
        private int _deviceWatchersCompletedCount = 0;

        // counter of device watchers completed
        private int _newDevicesCount = 0;

        private readonly Random _delay = new Random(DateTime.Now.Millisecond);

        /// <summary>
        /// Internal list with the actual nF Serial devices
        /// </summary>
        private readonly List<SerialDeviceInformation> _serialDevices;

        private readonly ConcurrentDictionary<string, CachedDeviceInfo> _devicesCache = new ConcurrentDictionary<string, CachedDeviceInfo>();

        // A pointer back to the calling app.  This is needed to reach methods and events there 
#if WINDOWS_UWP
       public static Windows.UI.Xaml.Application CallerApp { get; set; }
#else
        public static System.Windows.Application CallerApp { get; set; }
#endif

        public int BootTime { get; set; }

        /// <summary>
        /// Creates an Serial debug client
        /// </summary>
        public SerialPortManager(object callerApp, bool startDeviceWatchers = true, List<string> portBlackList = null, int bootTime = 2000)
        {
            _mapDeviceWatchersToDeviceSelector = new Dictionary<DeviceWatcher, string>();
            NanoFrameworkDevices = new ObservableCollection<NanoDeviceBase>();
            _serialDevices = new List<SerialDeviceInformation>();

            // set caller app property, if any
            if (callerApp != null)
            {

#if WINDOWS_UWP
                CallerApp = callerApp as Windows.UI.Xaml.Application;
#else
                CallerApp = callerApp as System.Windows.Application;
#endif
            };

            BootTime = bootTime;

            if (portBlackList != null)
            {
                PortBlackList = portBlackList;
            }

            Task.Factory.StartNew(() => {

                InitializeDeviceWatchers();

                if (startDeviceWatchers)
                {
                    StartSerialDeviceWatchers();
                }
            });
        }


        #region Device watchers initialization

        /*////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        Add a device watcher initialization method for each supported device that should be watched.
        That initialization method must be called from the InitializeDeviceWatchers() method above so the watcher is actually started.
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////*/

        /// <summary>
        /// Registers for Added, Removed, and Enumerated events on the provided deviceWatcher before adding it to an internal list.
        /// </summary>
        /// <param name="deviceWatcher">The device watcher to subscribe the events</param>
        /// <param name="deviceSelector">The AQS used to create the device watcher</param>
        private void AddDeviceWatcher(DeviceWatcher deviceWatcher, String deviceSelector)
        {
            deviceWatcher.Added += new TypedEventHandler<DeviceWatcher, DeviceInformation>(OnDeviceAdded);
            deviceWatcher.Removed += new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(OnDeviceRemoved);
            deviceWatcher.EnumerationCompleted += new TypedEventHandler<DeviceWatcher, object>(OnDeviceEnumerationComplete);

            _mapDeviceWatchersToDeviceSelector.Add(deviceWatcher, deviceSelector);
        }

        #endregion

        public override void ReScanDevices()
        {
            _newDevicesCount = 0;

            Task.Run(delegate
            {
                StopDeviceWatchersInternal();

                StartDeviceWatchersInternal();
            }).FireAndForget();
        }

        public override void StartDeviceWatchers()
        {
            if(!_watchersStarted)
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
            // Target all Serial Devices present on the system
            var deviceSelector = SerialDevice.GetDeviceSelector();

            // Other variations of GetDeviceSelector() usage are commented for reference
            //
            // Target a specific Serial Device using its VID and PID 
            // var deviceSelector = SerialDevice.GetDeviceSelectorFromUsbVidPid(0x2341, 0x0043);
            //
            // Target a specific Serial Device by its COM PORT Name - "COM3"
            // var deviceSelector = SerialDevice.GetDeviceSelector("COM3");
            //
            // Target a specific UART based Serial Device by its COM PORT Name (usually defined in ACPI) - "UART1"
            // var deviceSelector = SerialDevice.GetDeviceSelector("UART1");
            //

            // Create a device watcher to look for instances of the Serial Device that match the device selector
            // used earlier.

            var deviceWatcher = DeviceInformation.CreateWatcher(deviceSelector);

            // Allow the EventHandlerForDevice to handle device watcher events that relates or effects our device (i.e. device removal, addition, app suspension/resume)
            AddDeviceWatcher(deviceWatcher, deviceSelector);
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
            _watchersStarted = true;
            _deviceWatchersCompletedCount = 0;
            IsDevicesEnumerationComplete = false;

            foreach (DeviceWatcher deviceWatcher in _mapDeviceWatchersToDeviceSelector.Keys)
            {
                if ((deviceWatcher.Status != DeviceWatcherStatus.Started)
                    && (deviceWatcher.Status != DeviceWatcherStatus.EnumerationCompleted))
                {
                    deviceWatcher.Start();
                }
            }
        }

        /// <summary>
        /// Should be called on host app OnAppSuspension() event to properly handle that status.
        /// The DeviceWatchers must be stopped because device watchers will continue to raise events even if
        /// the app is in suspension, which is not desired (drains battery). The device watchers will be resumed once the app resumes too.
        /// </summary>
        public void AppSuspending()
        {
            if (_watchersStarted)
            {
                _watchersSuspended = true;
                StopDeviceWatchers();
            }
            else
            {
                _watchersSuspended = false;
            }
        }

        /// <summary>
        /// Should be called on host app OnAppResume() event to properly handle that status.
        /// See AppSuspending for why we are starting the device watchers again.
        /// </summary>
        public void AppResumed()
        {
            if (_watchersSuspended)
            {
                _watchersSuspended = false;
                StartDeviceWatchersInternal();
            }
        }

        /// <summary>
        /// Stops all device watchers.
        /// </summary>
        private void StopDeviceWatchersInternal()
        {
            // Stop all device watchers
            foreach (DeviceWatcher deviceWatcher in _mapDeviceWatchersToDeviceSelector.Keys)
            {
                if ((deviceWatcher.Status == DeviceWatcherStatus.Started)
                    || (deviceWatcher.Status == DeviceWatcherStatus.EnumerationCompleted))
                {
                    deviceWatcher.Stop();

                    // need to wait for the watcher to be stopped before proceeding to the next 
                    // 3 attempts
                    for(int i = 0; i < 3; i++)
                    {
                        if( deviceWatcher.Status == DeviceWatcherStatus.Stopped || deviceWatcher.Status == DeviceWatcherStatus.Aborted)
                        {
                            // this is OK now
                            break;
                        }

                        Thread.Sleep(300 * i);
                    }
                }
            }

            // Clear the list of devices so we don't have potentially disconnected devices around
            ClearDeviceEntries();

            // also clear nanoFramework devices list
            var devicesToRemove = NanoFrameworkDevices.Select(nanoDevice => ((NanoDevice<NanoSerialDevice>)nanoDevice).Device.DeviceInformation.DeviceInformation.Id).ToList();

            foreach (var deviceId in devicesToRemove)
            {
                // get device...
                var device = FindNanoFrameworkDevice(deviceId);

                // ... and remove it from collection
                NanoFrameworkDevices.Remove(device);

                device?.DebugEngine?.StopProcessing();
                device?.DebugEngine?.Dispose();

                device?.Disconnect();
                // This closes the handle to the device
                ((NanoDevice<NanoSerialDevice>)device)?.Dispose();
            }

            _watchersStarted = false;
        }

        #endregion


        #region Methods to manage device list add, remove, etc

        /// <summary>
        /// Creates a DeviceListEntry for a device and adds it to the list of devices
        /// </summary>
        /// <param name="deviceInformation">DeviceInformation on the device to be added to the list</param>
        /// <param name="deviceSelector">The AQS used to find this device</param>
        private async Task AddDeviceToListAsync(DeviceInformation deviceInformation, String deviceSelector)
        {
            // search the device list for a device with a matching interface ID
            var serialMatch = FindDevice(deviceInformation.Id);

            // Add the device if it's new
            if (serialMatch == null)
            {
                var serialDevice = new SerialDeviceInformation(deviceInformation, deviceSelector);

                OnLogMessageAvailable(NanoDevicesEventSource.Log.CandidateDevice(deviceInformation.Id));

                // search the nanoFramework device list for a device with a matching interface ID
                var nanoFrameworkDeviceMatch = FindNanoFrameworkDevice(deviceInformation.Id);

                if (nanoFrameworkDeviceMatch == null)
                {
                    // Create a new element for this device and...
                    var newNanoFrameworkDevice = new NanoDevice<NanoSerialDevice>();
                    newNanoFrameworkDevice.Device.DeviceInformation = new SerialDeviceInformation(deviceInformation, deviceSelector);
                    newNanoFrameworkDevice.ConnectionPort = new SerialPort(this, newNanoFrameworkDevice);
                    newNanoFrameworkDevice.Transport = TransportType.Serial;

                    await Task.Delay(100);

                    if (await newNanoFrameworkDevice.ConnectionPort.ConnectDevice())
                    {
                        if (await CheckValidNanoFrameworkSerialDeviceAsync(newNanoFrameworkDevice))
                        {
                            //add device to the collection
                            NanoFrameworkDevices.Add(newNanoFrameworkDevice);

                            _serialDevices.Add(serialDevice);

                            OnLogMessageAvailable(NanoDevicesEventSource.Log.ValidDevice($"{newNanoFrameworkDevice.Description} {newNanoFrameworkDevice.Device.DeviceInformation.DeviceInformation.Id}"));
                        }
                        else
                        {
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
                            await Task.Delay(BootTime + delay);

                            OnLogMessageAvailable(NanoDevicesEventSource.Log.CheckingValidDevice($" {newNanoFrameworkDevice.Device.DeviceInformation.DeviceInformation.Id} *** 2nd attempt ***"));

                            if (await newNanoFrameworkDevice.ConnectionPort.ConnectDevice())
                            {
                                if (await CheckValidNanoFrameworkSerialDeviceAsync(newNanoFrameworkDevice))
                                {
                                    //add device to the collection
                                    NanoFrameworkDevices.Add(newNanoFrameworkDevice);

                                    _serialDevices.Add(serialDevice);

                                    OnLogMessageAvailable(NanoDevicesEventSource.Log.ValidDevice($"{newNanoFrameworkDevice.Description} {newNanoFrameworkDevice.Device.DeviceInformation.DeviceInformation.Id}"));
                                }
                                else
                                {
                                    OnLogMessageAvailable(NanoDevicesEventSource.Log.QuitDevice(deviceInformation.Id));
                                }
                            }
                            else
                            {
                                OnLogMessageAvailable(NanoDevicesEventSource.Log.QuitDevice(deviceInformation.Id));
                            }
                        }
                    }
                    else
                    {
                        OnLogMessageAvailable(NanoDevicesEventSource.Log.QuitDevice(deviceInformation.Id));
                    }

                    // subtract devices count
                    _newDevicesCount--;

                    // check if we are done processing arriving devices
                    if (_newDevicesCount == 0)
                    {
                        ProcessDeviceEnumerationComplete();
                    }
                }
            }
        }

        private void RemoveDeviceFromList(string deviceId)
        {
            // Removes the device entry from the internal list; therefore the UI
            var deviceEntry = FindDevice(deviceId);

            OnLogMessageAvailable(NanoDevicesEventSource.Log.DeviceDeparture(deviceId));

            _serialDevices.Remove(deviceEntry);

            // get device...
            var device = FindNanoFrameworkDevice(deviceId);

            // ... and remove it from collection
            NanoFrameworkDevices.Remove(device);

            device?.DebugEngine?.StopProcessing();
            device?.DebugEngine?.Dispose();
        }

        private void ClearDeviceEntries()
        {
            _serialDevices.Clear();
        }

        /// <summary>
        /// Searches through the existing list of devices for the first DeviceListEntry that has
        /// the specified device Id.
        /// </summary>
        /// <param name="deviceId">Id of the device that is being searched for</param>
        /// <returns>DeviceListEntry that has the provided Id; else a nullptr</returns>
        internal SerialDeviceInformation FindDevice(String deviceId)
        {
            if (deviceId != null)
            {
                foreach (SerialDeviceInformation entry in _serialDevices)
                {
                    if (entry.DeviceInformation.Id == deviceId)
                    {
                        return entry;
                    }
                }
            }

            return null;
        }

        private NanoDeviceBase FindNanoFrameworkDevice(string deviceId)
        {
            if (deviceId != null)
            {
                // SerialMatch.Device.DeviceInformation
                return NanoFrameworkDevices.FirstOrDefault(d => ((d as NanoDevice<NanoSerialDevice>).Device.DeviceInformation ).DeviceInformation.Id == deviceId);
            }

            return null;
        }

        /// <summary>
        /// Remove the device from the device list 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="deviceInformationUpdate"></param>
        private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate deviceInformationUpdate)
        {
            RemoveDeviceFromList(deviceInformationUpdate.Id);
        }

        /// <summary>
        /// This function will add the device to the listOfDevices
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="deviceInformation"></param>
        private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation deviceInformation)
        {
            // device black listed
            // discard known system and unusable devices
            // 
            if (
               deviceInformation.Id.StartsWith(@"\\?\ACPI") ||

               // reported in https://github.com/nanoframework/Home/issues/332
               // COM ports from Broadcom 20702 Bluetooth adapter
               deviceInformation.Id.Contains(@"VID_0A5C+PID_21E1") ||

               // reported in https://nanoframework.slack.com/archives/C4MGGBH1P/p1531660736000055?thread_ts=1531659631.000021&cid=C4MGGBH1P
               // COM ports from Broadcom 20702 Bluetooth adapter
               deviceInformation.Id.Contains(@"VID&00010057_PID&0023") ||

               // reported in Discord channel
               deviceInformation.Id.Contains(@"VID&0001009e_PID&400a") ||

               // this seems to cover virtual COM ports from Bluetooth devices
               deviceInformation.Id.Contains("BTHENUM")
               )
            {
                OnLogMessageAvailable(NanoDevicesEventSource.Log.DroppingBlackListedDevice(deviceInformation.Id));

                // don't even bother with this one
                return;
            }

            OnLogMessageAvailable(NanoDevicesEventSource.Log.DeviceArrival(deviceInformation.Id));

            _newDevicesCount++;

            Task.Run(async delegate
            {
                await AddDeviceToListAsync(deviceInformation, _mapDeviceWatchersToDeviceSelector[sender]);
            }).FireAndForget();
        }

        #endregion


        #region Handlers and events for Device Enumeration Complete 

        private void OnDeviceEnumerationComplete(DeviceWatcher sender, object args)
        {
            // add another device watcher completed
            _deviceWatchersCompletedCount++;
        }

        private void ProcessDeviceEnumerationComplete()
        {
            OnLogMessageAvailable(NanoDevicesEventSource.Log.SerialDeviceEnumerationCompleted(NanoFrameworkDevices.Count));

            // all watchers have completed enumeration
            IsDevicesEnumerationComplete = true;

            // fire event that Serial enumeration is complete 
            OnDeviceEnumerationCompleted();
        }

        private async Task<bool> CheckValidNanoFrameworkSerialDeviceAsync(NanoDevice<NanoSerialDevice> device)
        {
            bool validDevice = false;
            bool isKnownDevice = false;
            string deviceId = null;

            try
            {
                if (device.DebugEngine == null)
                {
                    device.CreateDebugEngine();
                }

                // get access to Windows.Devices.SerialDevice object
                // so we can set it's BaudRate property
                var serialDevice = (SerialDevice)device.DeviceBase;

                // sanity check for invalid or null device base
                if (serialDevice != null)
                {
                    // store device ID
                    deviceId = device.Device.DeviceInformation.DeviceInformation.Id;

                    // check against black list
                    if (PortBlackList.Contains(serialDevice.PortName))
                    {
                        OnLogMessageAvailable(NanoDevicesEventSource.Log.DroppingBlackListedDevice(deviceId));
                    }
                    else
                    {
                        // check if this device is on cache
                        isKnownDevice = _devicesCache.TryGetValue(deviceId, out var cachedDevice);

                        // need to go through all the valid baud rates: 921600, 460800 and 115200.
                        foreach (uint baudRate in SerialPort.ValidBaudRates)
                        {
                            if (isKnownDevice)
                            {
                                // OK to go with stored cache
                                serialDevice.BaudRate = cachedDevice.BaudRate;
                            }
                            else
                            {
                                serialDevice.BaudRate = baudRate;
                            }

                            OnLogMessageAvailable(NanoDevicesEventSource.Log.CheckingValidDevice($" {deviceId} @ { baudRate }"));

                            // try to "just" connect to the device meaning...
                            // ... don't request capabilities or force anything except the absolute minimum required, plus...
                            // ... it's OK to use a very short timeout as we'll be exchanging really short packets with the device
                            if (await device.DebugEngine.ConnectAsync(
                                200,
                                false,
                                ConnectionSource.Unknown,
                                false))
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

                                if (device.DebugEngine.ConnectionSource == ConnectionSource.nanoBooter)
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
                                    var deviceInfo = targetInfoPropertiesPolicy.Execute(() => { if (device.DebugEngine != null) { return device.DebugEngine.GetTargetInfo(); } else { return new CLRCapabilities.TargetInfoProperties(); } });

                                    if (!string.IsNullOrEmpty(deviceInfo.TargetName))
                                    {
                                        device.TargetName = deviceInfo.TargetName;
                                        device.Platform = deviceInfo.Platform;
                                    }
                                }

                                if (string.IsNullOrEmpty(device.TargetName)
                                    || string.IsNullOrEmpty(device.Platform))
                                {
                                    OnLogMessageAvailable(NanoDevicesEventSource.Log.CriticalError($"ERROR: {device.Device.DeviceInformation.DeviceInformation.Id} failed to get target information"));

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
                }

                if (validDevice)
                {
                    // there should be a valid nanoFramework device at the other end

                    device.SerialNumber = GetSerialNumber(deviceId);

                    // set valid baud rate from device detection
                    ((SerialPort)device.ConnectionPort).BaudRate = serialDevice.BaudRate;

                    // store connection ID
                    device.ConnectionId = serialDevice.PortName;

                    // store device in cache
                    var cachedDevice = new CachedDeviceInfo(
                        device.TargetName,
                        device.Platform,
                        serialDevice.BaudRate);

                    _devicesCache.TryAdd(
                        deviceId,
                        cachedDevice);
                }
                else
                {
                    // remove from cache
                    _devicesCache.TryRemove(deviceId, out var dummy);
                }
 
                Task.Factory.StartNew(() =>
                {
                    // perform the Dispose() call on a Task
                    // this is required to be able to actually close devices that get stuck with pending tasks on the in/output streams

                    device.Disconnect();
                    // This closes the handle to the device
                    device?.Dispose();
                    device = null;

                }).FireAndForget();
            }
            catch
            {
                // "catch all" required because the device open & check calls might fail for a number of reasons
                // if there is a deviceID, remove it from cache, just in case
                if (deviceId != null)
                {
                    _devicesCache.TryRemove(deviceId, out var dummy);
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

        #endregion

        public static string GetSerialNumber(string value)
        {
            // typical ID string is \\?\USB#VID_0483&PID_5740#NANO_3267335D#{86e0d1e0-8089-11d0-9ce4-08003e301f73}

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
    }
}
