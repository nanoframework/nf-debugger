//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.Win32;
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.Serial;
using nanoFramework.Tools.Debugger.WireProtocol;
using Polly;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    public partial class PortSerialManager : PortBase
    {
        private readonly DeviceWatcher _deviceWatcher = new();

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

        public int BootTime { get; set; }

        /// <summary>
        /// Creates an Serial debug client
        /// </summary>
        public PortSerialManager(bool startDeviceWatchers = true, List<string> portExclusionList = null, int bootTime = 3000)
        {
            NanoFrameworkDevices = new ObservableCollection<NanoDeviceBase>();
            _serialDevices = new List<SerialDeviceInformation>();

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
            _newDevicesCount = 0;

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

            _deviceWatcher.Start();

            _watchersStarted = true;

            _deviceWatchersCompletedCount = 0;
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
            ClearDeviceEntries();

            // also clear nanoFramework devices list
            var devicesToRemove = NanoFrameworkDevices.Select(nanoDevice => ((NanoDevice<NanoSerialDevice>)nanoDevice).DeviceId).ToList();

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
        /// <param name="deviceId">The AQS used to find this device</param>
        private void AddDeviceToListAsync(String deviceId)
        {
            // search the device list for a device with a matching interface ID
            var serialMatch = FindDevice(deviceId);

            // Add the device if it's new
            if (serialMatch == null)
            {
                var serialDevice = new SerialDeviceInformation(deviceId);

                OnLogMessageAvailable(NanoDevicesEventSource.Log.CandidateDevice(deviceId));

                // search the nanoFramework device list for a device with a matching interface ID
                var nanoFrameworkDeviceMatch = FindNanoFrameworkDevice(deviceId);

                if (nanoFrameworkDeviceMatch == null)
                {
                    // Create a new element for this device and...
                    var newNanoFrameworkDevice = new NanoDevice<NanoSerialDevice>();
                    newNanoFrameworkDevice.DeviceId = deviceId;
                    newNanoFrameworkDevice.ConnectionPort = new PortSerial(this, newNanoFrameworkDevice);
                    newNanoFrameworkDevice.Transport = TransportType.Serial;

                    if (newNanoFrameworkDevice.ConnectionPort.ConnectDevice())
                    {
                        if (CheckValidNanoFrameworkSerialDevice(newNanoFrameworkDevice))
                        {
                            //add device to the collection
                            NanoFrameworkDevices.Add(newNanoFrameworkDevice);

                            _serialDevices.Add(serialDevice);

                            OnLogMessageAvailable(NanoDevicesEventSource.Log.ValidDevice($"{newNanoFrameworkDevice.Description} {newNanoFrameworkDevice.DeviceId}"));
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

                            if (newNanoFrameworkDevice.ConnectionPort.ConnectDevice())
                            {
                                if (CheckValidNanoFrameworkSerialDevice(newNanoFrameworkDevice))
                                {
                                    //add device to the collection
                                    NanoFrameworkDevices.Add(newNanoFrameworkDevice);

                                    _serialDevices.Add(serialDevice);

                                    OnLogMessageAvailable(NanoDevicesEventSource.Log.ValidDevice($"{newNanoFrameworkDevice.Description} {newNanoFrameworkDevice.DeviceId}"));
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

        public override void DisposeDevice(string instanceId)
        {
            var deviceToDispose = NanoFrameworkDevices.FirstOrDefault(nanoDevice => ((NanoDevice<NanoSerialDevice>)nanoDevice).DeviceId == instanceId);

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
                    if (entry.InstanceId == deviceId)
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
                return NanoFrameworkDevices.FirstOrDefault(d => ((d as NanoDevice<NanoSerialDevice>).DeviceId) == deviceId);
            }

            return null;
        }

        /// <summary>
        /// Remove the device from the device list 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="deviceInformationUpdate"></param>
        private void OnDeviceRemoved(object sender, string serialPort)
        {
            RemoveDeviceFromList(serialPort);
        }

        // TODO
        /// <summary>
        /// This function will add the device to the listOfDevices
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="deviceInformation"></param>
        private void OnDeviceAdded(object sender, string serialPort)
        {
            // check against exclusion list
            if (PortExclusionList.Contains(serialPort))
            {
                OnLogMessageAvailable(NanoDevicesEventSource.Log.DroppingDeviceToExclude(serialPort));
                return;
            }

            // discard known system and other rogue devices
            RegistryKey portKeyInfo = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\COM Name Arbiter\Devices");
            if (portKeyInfo != null)
            {
                var portInfo = (string)portKeyInfo.GetValue(serialPort);

                if (portInfo != null)
                {
                    Debug.WriteLine($"{nameof(OnDeviceAdded)}: port {serialPort}, portinfo: {portInfo}");

                    // make  it upper case for comparison
                    portInfo = portInfo.ToUpperInvariant();

                    if (
                       portInfo.StartsWith(@"\\?\ACPI") ||

                       // reported in https://github.com/nanoframework/Home/issues/332
                       // COM ports from Broadcom 20702 Bluetooth adapter
                       portInfo.Contains(@"VID_0A5C+PID_21E1") ||

                       // reported in https://nanoframework.slack.com/archives/C4MGGBH1P/p1531660736000055?thread_ts=1531659631.000021&cid=C4MGGBH1P
                       // COM ports from Broadcom 20702 Bluetooth adapter
                       portInfo.Contains(@"VID&00010057_PID&0023") ||

                       // reported in Discord channel
                       portInfo.Contains(@"VID&0001009E_PID&400A") ||

                       // this seems to cover virtual COM ports from Bluetooth devices
                       portInfo.Contains("BTHENUM") ||

                       // this seems to cover virtual COM ports by ELTIMA 
                       portInfo.Contains("EVSERIAL")
                       )
                    {
                        OnLogMessageAvailable(NanoDevicesEventSource.Log.DroppingDeviceToExclude(serialPort));

                        // don't even bother with this one
                        return;
                    }
                }
            }

            OnLogMessageAvailable(NanoDevicesEventSource.Log.DeviceArrival(serialPort));

            _newDevicesCount++;

            Task.Run(() =>
            {
                AddDeviceToListAsync(serialPort);
            });
        }

        #endregion


        #region Handlers and events for Device Enumeration Complete 

        private void ProcessDeviceEnumerationComplete()
        {
            OnLogMessageAvailable(NanoDevicesEventSource.Log.SerialDeviceEnumerationCompleted(NanoFrameworkDevices.Count));

            // all watchers have completed enumeration
            IsDevicesEnumerationComplete = true;

            // fire event that Serial enumeration is complete 
            OnDeviceEnumerationCompleted();
        }

        private bool CheckValidNanoFrameworkSerialDevice(NanoDevice<NanoSerialDevice> device)
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
                            device.CreateDebugEngine(NanoSerialDevice.SafeDefaultTimeout);
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

                        if (!((SerialPort)device.DeviceBase).IsOpen)
                        {
                            ((SerialPort)device.DeviceBase).Open();
                        }

                        // better flush the UART FIFOs
                        ((SerialPort)device.DeviceBase).DiscardInBuffer();
                        ((SerialPort)device.DeviceBase).DiscardOutBuffer();

                        OnLogMessageAvailable(NanoDevicesEventSource.Log.CheckingValidDevice($" {deviceId} @ { baudRate }"));

                        // try to "just" connect to the device meaning...
                        // ... don't request capabilities or force anything except the absolute minimum required, plus...
                        // ... it's OK to use a very short timeout as we'll be exchanging really short packets with the device
                        if (device.DebugEngine.Connect(
                            500,
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
            catch
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
    }
}
