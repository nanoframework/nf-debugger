//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.Serial;
using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    public partial class SerialPort : PortBase, IPort
    {
        // dictionary with mapping between Serial device watcher and the device ID
        private Dictionary<DeviceWatcher, string> _mapDeviceWatchersToDeviceSelector;

        // Serial device watchers suspended flag
        private bool _watchersSuspended = false;

        // Serial device watchers started flag
        private bool _watchersStarted = false;

        // counter of device watchers completed
        private int _deviceWatchersCompletedCount = 0;

        // R/W operation cancellation tokens objects
        private CancellationTokenSource ReadCancellationTokenSource;
        private CancellationTokenSource SendCancellationTokenSource;
        private Object ReadCancelLock = new Object();
        private Object SendCancelLock = new Object();

        /// <summary>
        /// Internal list with the actual nF Serial devices
        /// </summary>
        List<SerialDeviceInformation> _serialDevices;

        /// <summary>
        /// Internal list of the tentative devices to be checked as valid nanoFramework devices
        /// </summary>
        private List<NanoDeviceBase> _tentativeNanoFrameworkDevices = new List<NanoDeviceBase>();

        /// <summary>
        /// Creates an Serial debug client
        /// </summary>
        public SerialPort(object callerApp)
        {
            _mapDeviceWatchersToDeviceSelector = new Dictionary<DeviceWatcher, String>();
            NanoFrameworkDevices = new ObservableCollection<NanoDeviceBase>();
            _serialDevices = new List<Serial.SerialDeviceInformation>();

            // set caller app property, if any
            if (callerApp != null)
            {

#if WINDOWS_UWP
                EventHandlerForSerialDevice.CallerApp = callerApp as Windows.UI.Xaml.Application;
#else
                EventHandlerForSerialDevice.CallerApp = callerApp as System.Windows.Application;
#endif
            };

            Task.Factory.StartNew(() => {
                StartSerialDeviceWatchers();
            });

            ResetReadCancellationTokenSource();
            ResetSendCancellationTokenSource();

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
            InitializeDeviceWatchers();
            StartDeviceWatchers();
        }

        /// <summary>
        /// Starts all device watchers including ones that have been individually stopped.
        /// </summary>
        private void StartDeviceWatchers()
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
                StartDeviceWatchers();
            }
        }

        /// <summary>
        /// Stops all device watchers.
        /// </summary>
        private void StopDeviceWatchers()
        {
            // Stop all device watchers
            foreach (DeviceWatcher deviceWatcher in _mapDeviceWatchersToDeviceSelector.Keys)
            {
                if ((deviceWatcher.Status == DeviceWatcherStatus.Started)
                    || (deviceWatcher.Status == DeviceWatcherStatus.EnumerationCompleted))
                {
                    deviceWatcher.Stop();
                }
            }

            // Clear the list of devices so we don't have potentially disconnected devices around
            ClearDeviceEntries();

            _watchersStarted = false;
        }

        #endregion


        #region Methods to manage device list add, remove, etc

        /// <summary>
        /// Creates a DeviceListEntry for a device and adds it to the list of devices
        /// </summary>
        /// <param name="deviceInformation">DeviceInformation on the device to be added to the list</param>
        /// <param name="deviceSelector">The AQS used to find this device</param>
        private async void AddDeviceToList(DeviceInformation deviceInformation, String deviceSelector)
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
                deviceInformation.Id.Contains(@"VID&00010057_PID&0023")
               )
            {
                OnLogMessageAvailable(NanoDevicesEventSource.Log.DroppingBlackListedDevice(deviceInformation.Id));

                // don't even bother with these
                return;
            }

            OnLogMessageAvailable(NanoDevicesEventSource.Log.DeviceArrival(deviceInformation.Id));

            // search the device list for a device with a matching interface ID
            var serialMatch = FindDevice(deviceInformation.Id);

            // Add the device if it's new
            if (serialMatch == null)
            {
                var serialDevice = new SerialDeviceInformation(deviceInformation, deviceSelector);
                _serialDevices.Add(serialDevice);

                OnLogMessageAvailable(NanoDevicesEventSource.Log.CandidateDevice(deviceInformation.Id));

                // search the nanoFramework device list for a device with a matching interface ID
                var nanoFrameworkDeviceMatch = FindNanoFrameworkDevice(deviceInformation.Id);

                if (nanoFrameworkDeviceMatch == null)
                {
                    // Create a new element for this device and...
                    var newNanoFrameworkDevice = new NanoDevice<NanoSerialDevice>();
                    newNanoFrameworkDevice.Device.DeviceInformation = new SerialDeviceInformation(deviceInformation, deviceSelector);
                    newNanoFrameworkDevice.Parent = this;
                    newNanoFrameworkDevice.Transport = TransportType.Serial;

                    // ... add it to the collection of tentative devices
                    _tentativeNanoFrameworkDevices.Add(newNanoFrameworkDevice as NanoDeviceBase);

                    // perform check for valid nanoFramework device is this is not the initial enumeration
                    if (IsDevicesEnumerationComplete)
                    {
                        if (await CheckValidNanoFrameworkSerialDeviceAsync(newNanoFrameworkDevice.Device.DeviceInformation))
                        {
                            // the device info was updated above, need to get it from the tentative devices collection

                            //add device to the collection
                            NanoFrameworkDevices.Add(FindNanoFrameworkDevice(newNanoFrameworkDevice.Device.DeviceInformation.DeviceInformation.Id));

                            OnLogMessageAvailable(NanoDevicesEventSource.Log.ValidDevice($"{newNanoFrameworkDevice.Description} {newNanoFrameworkDevice.Device.DeviceInformation.DeviceInformation.Id}"));

                            // done here, clear tentative list
                            _tentativeNanoFrameworkDevices.Clear();

                            // done here
                            return;
                        }

                        // clear tentative list
                        _tentativeNanoFrameworkDevices.Clear();

                        OnLogMessageAvailable(NanoDevicesEventSource.Log.QuitDevice(deviceInformation.Id));

                        _serialDevices.Remove(serialDevice);
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
        private SerialDeviceInformation FindDevice(String deviceId)
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
                var device = NanoFrameworkDevices.FirstOrDefault(d => ((d as NanoDevice<NanoSerialDevice>).Device.DeviceInformation as SerialDeviceInformation).DeviceInformation.Id == deviceId);

                if (device == null)
                {
                    // try now in tentative list
                    return _tentativeNanoFrameworkDevices.FirstOrDefault(d => ((d as NanoDevice<NanoSerialDevice>).Device.DeviceInformation as SerialDeviceInformation).DeviceInformation.Id == deviceId);
                }
                else
                {
                    return device;
                }
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
            AddDeviceToList(deviceInformation, _mapDeviceWatchersToDeviceSelector[sender]);
        }

        #endregion


        #region Handlers and events for Device Enumeration Complete 

        private void OnDeviceEnumerationComplete(DeviceWatcher sender, object args)
        {
            // add another device watcher completed
            _deviceWatchersCompletedCount++;

            if (_deviceWatchersCompletedCount == _mapDeviceWatchersToDeviceSelector.Count)
            {
                Task.Factory.StartNew(async () =>
                {
                    // prepare a list of devices that are to be removed if they are deemed as not valid nanoFramework devices
                    var devicesToRemove = new List<NanoDeviceBase>();

                    foreach (NanoDeviceBase device in _tentativeNanoFrameworkDevices)
                    {
                        var nFDeviceIsValid = await CheckValidNanoFrameworkSerialDeviceAsync(((NanoDevice<NanoSerialDevice>)device).Device.DeviceInformation).ConfigureAwait(true);

                        if (nFDeviceIsValid)
                        {
                            OnLogMessageAvailable(NanoDevicesEventSource.Log.ValidDevice($"{device.Description} {(((NanoDevice<NanoSerialDevice>)device).Device.DeviceInformation.DeviceInformation.Id)}"));

                            NanoFrameworkDevices.Add(device);
                        }
                        else
                        {
                            OnLogMessageAvailable(NanoDevicesEventSource.Log.QuitDevice(((NanoDevice<NanoSerialDevice>)device).Device.DeviceInformation.DeviceInformation.Id));
                        }
                    }

                    // all watchers have completed enumeration
                    IsDevicesEnumerationComplete = true;

                    // clean list of tentative nanoFramework Devices
                    _tentativeNanoFrameworkDevices.Clear();

                    OnLogMessageAvailable(NanoDevicesEventSource.Log.SerialDeviceEnumerationCompleted(NanoFrameworkDevices.Count));

                    // fire event that Serial enumeration is complete 
                    OnDeviceEnumerationCompleted();

                }).FireAndForget();
            }
        }

        private async Task<bool> CheckValidNanoFrameworkSerialDeviceAsync(SerialDeviceInformation deviceInformation)
        {
            // get name
            var name = deviceInformation.DeviceInformation.Name;
            var serialNumber = GetSerialNumber(deviceInformation.DeviceInformation.Id);

            OnLogMessageAvailable(NanoDevicesEventSource.Log.CheckingValidDevice(deviceInformation.DeviceInformation.Id));

            var tentativeDevice = await SerialDevice.FromIdAsync(deviceInformation.DeviceInformation.Id);

            try
            {
                // Device could have been blocked by user or the device has already been opened by another app.
                if (tentativeDevice != null)
                {
                    // adjust settings for serial port
                    tentativeDevice.BaudRate = 115200;
                    tentativeDevice.DataBits = 8;

                    /////////////////////////////////////////////////////////////
                    // need to FORCE the parity setting to _NONE_ because        
                    // the default on the current ST Link is different causing 
                    // the communication to fail
                    /////////////////////////////////////////////////////////////
                    tentativeDevice.Parity = SerialParity.None;

                    tentativeDevice.WriteTimeout = TimeSpan.FromMilliseconds(1000);
                    tentativeDevice.ReadTimeout = TimeSpan.FromMilliseconds(1000);

                    if (serialNumber != null && serialNumber.Contains("NANO_"))
                    {
                        var device = FindNanoFrameworkDevice(deviceInformation.DeviceInformation.Id);

                        if (device != null)
                        {
                            device.Description = serialNumber + " @ " + tentativeDevice.PortName;

                            // should be a valid nanoFramework device, done here
                            return true;
                        }
                        else
                        {
                            OnLogMessageAvailable(NanoDevicesEventSource.Log.CriticalError($"Couldn't find nano device {EventHandlerForSerialDevice.Current.DeviceInformation.Id} with serial {serialNumber}"));
                        }
                    }
                    else
                    {
                        // need an extra check on this because this can be 'just' a regular COM port without any nanoFramework device behind

                        // fill in description for this device
                        var device = FindNanoFrameworkDevice(deviceInformation.DeviceInformation.Id);

                        // need an extra check on this because this can be 'just' a regular COM port without any nanoFramework device behind
                        var connectionResult = await PingDeviceLocalAsync(tentativeDevice);

                        if (connectionResult)
                        {
                            // should be a valid nanoFramework device
                            device.Description = name + " @ " + tentativeDevice.PortName;

                            // done here
                            return true;
                        }
                        else
                        {
                            // doesn't look like a nanoFramework device
                            return false;
                        }
                    }
                }
                else
                {

                    // Most likely the device is opened by another app, but cannot be sure
                    OnLogMessageAvailable(NanoDevicesEventSource.Log.CriticalError($"Unknown error, possibly opened by another app : {deviceInformation.DeviceInformation.Id}"));
                }
            }
            // catch all because the device open might fail for a number of reasons
            catch (Exception ex)
            {
            }
            finally
            {
                // dispose on a Task to perform the Dispose()
                // this is required to be able to actually close devices that get stuck with pending tasks on the in/output streams
                var closeTask = Task.Factory.StartNew(() =>
                {
                    // This closes the handle to the device
                    tentativeDevice?.Dispose();
                    tentativeDevice = null;
                });
            }

            // default to false
            return false;
        }

        private async Task<bool> PingDeviceLocalAsync(SerialDevice tentativeDevice)
        {
            // dev note: this temporary connection to the device, because it actually connects with the device and sends data, has to be carried on it's own without reusing anything from the EventHandlerForSerialDevice otherwise it would break it
            // this operation sends a valid ping request and waits for a reply from the connected COM port
            // considering that the Ping request is valid and properly formatted we are just checking if there is a reply from the device and if it has the expected length
            // this might look as an oversimplification or simplistic but it's quite safe

            try
            {
                // fake Ping header
                byte[] pingHeader = new byte[] {
                78,
                70,
                80,
                75,
                84,
                86,
                49,
                0,
                240,
                240,
                187,
                218,
                148,
                185,
                67,
                183,
                0,
                0,
                0,
                0,
                191,
                130,
                0,
                0,
                0,
                32,
                0,
                0,
                8,
                0,
                0,
                0,
            };

                // fake Ping payload
                byte[] pingPayload = new byte[] {
                0x02,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
                0x00,
            };

                using (var cts = new CancellationTokenSource())
                {
                    DataWriter outputStreamWriter = new DataWriter(tentativeDevice.OutputStream);
                    DataReader inputStreamReader = new DataReader(tentativeDevice.InputStream);
                    Task<UInt32> storeAsyncTask;
                    Task<UInt32> loadAsyncTask;

                    try
                    {
                        ///////////////////////////////////////////////////
                        // write pingHeader to device
                        outputStreamWriter.WriteBytes(pingHeader);

                        storeAsyncTask = outputStreamWriter.StoreAsync().AsTask(cts.Token.AddTimeout(new TimeSpan(0, 0, 1)));

                        var txBytes = await storeAsyncTask;

                        //////////////////////////////////////////////////
                        // write pingPayload to device
                        outputStreamWriter.WriteBytes(pingPayload);

                        storeAsyncTask = outputStreamWriter.StoreAsync().AsTask(cts.Token.AddTimeout(new TimeSpan(0, 0, 1)));

                        txBytes = await storeAsyncTask;

                        //////////////////////////////////////////////////
                        // read answer (32 bytes)
                        loadAsyncTask = inputStreamReader.LoadAsync(32).AsTask(cts.Token.AddTimeout(new TimeSpan(0, 0, 1)));

                        UInt32 bytesRead = await loadAsyncTask;

                        if (bytesRead == 32)
                        {
                            // at this point we are just happy to get the expected number of bytes from a valid nanoDevice
                            return true;
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // this is expected to happen, don't do anything with this
                    }
                    catch (Exception ex)
                    {
                        throw ex;
                    }
                    finally
                    {
                        // detach stream
                        outputStreamWriter?.DetachStream();
                        outputStreamWriter = null;

                        // detach stream
                        inputStreamReader?.DetachStream();
                        inputStreamReader = null;
                    }
                }
            }
            catch { }

            // default to false
            return false;
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


        public async Task<bool> ConnectDeviceAsync(NanoDeviceBase device)
        {
            bool connectFlag = await ConnectSerialDeviceAsync((device as NanoDevice<NanoSerialDevice>).Device.DeviceInformation as SerialDeviceInformation);

            if(connectFlag && device.DeviceBase == null)
            {
                device.DeviceBase = EventHandlerForSerialDevice.Current.Device;
            }

            return connectFlag;
        }

        private async Task<bool> ConnectSerialDeviceAsync(SerialDeviceInformation serialDeviceInfo)
        {
            // try to determine if we already have this device opened.
            if (EventHandlerForSerialDevice.Current.Device != null)
            {
                // device matches
                if (EventHandlerForSerialDevice.Current.DeviceInformation == serialDeviceInfo.DeviceInformation)
                {
                    return true;
                }
            }

            bool openDeviceResult = await EventHandlerForSerialDevice.Current.OpenDeviceAsync(serialDeviceInfo.DeviceInformation, serialDeviceInfo.DeviceSelector);

            if (openDeviceResult)
            {
                OnLogMessageAvailable(NanoDevicesEventSource.Log.OpenDevice(serialDeviceInfo.DeviceInformation.Id));
            }
            else
            {
                // Most likely the device is opened by another app, but cannot be sure
                OnLogMessageAvailable(NanoDevicesEventSource.Log.CriticalError($"Unknown error opening {serialDeviceInfo.DeviceInformation.Id}, possibly opened by another app"));
            }

            return openDeviceResult;
        }

        public void DisconnectDevice(NanoDeviceBase device)
        {
            var deviceCheck = FindDevice(((device as NanoDevice<NanoSerialDevice>).Device.DeviceInformation as SerialDeviceInformation).DeviceInformation.Id);

            if (EventHandlerForSerialDevice.Current != null && EventHandlerForSerialDevice.Current.DeviceInformation.Id == deviceCheck?.DeviceInformation.Id)
            {
                // disconnecting the current device

                // cancel all IO operations
                CancelAllIoTasks();

                OnLogMessageAvailable(NanoDevicesEventSource.Log.CloseDevice(deviceCheck?.DeviceInformation.Id));

                // close device
                EventHandlerForSerialDevice.Current.CloseDevice();

                // stop and dispose DebugEgine if instantiated
                device.DebugEngine?.Stop();
                device.DebugEngine?.Dispose();
                device.DebugEngine = null;
            }
        }

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

        private void CancelReadTask()
        {
            lock (ReadCancelLock)
            {
                if (ReadCancellationTokenSource != null)
                {
                    if (!ReadCancellationTokenSource.IsCancellationRequested)
                    {
                        ReadCancellationTokenSource.Cancel();

                        // Existing IO already has a local copy of the old cancellation token so this reset won't affect it
                        ResetReadCancellationTokenSource();
                    }
                }
            }
        }

        private void CancelWriteTask()
        {
            lock (SendCancelLock)
            {
                if (SendCancellationTokenSource != null)
                {
                    if (!SendCancellationTokenSource.IsCancellationRequested)
                    {
                        SendCancellationTokenSource.Cancel();

                        // Existing IO already has a local copy of the old cancellation token so this reset won't affect it
                        ResetSendCancellationTokenSource();
                    }
                }
            }
        }

        private void CancelAllIoTasks()
        {
            CancelReadTask();
            CancelWriteTask();
        }

        private void ResetReadCancellationTokenSource()
        {
            // Create a new cancellation token source so that can cancel all the tokens again
            ReadCancellationTokenSource = new CancellationTokenSource();

            // Hook the cancellation callback (called whenever Task.cancel is called)
            // TODO this probably could be used to notify the debug engine and others of the cancellation
            //ReadCancellationTokenSource.Token.Register(() => NotifyReadCancelingTask());
        }

        private void ResetSendCancellationTokenSource()
        {
            // Create a new cancellation token source so that can cancel all the tokens again
            SendCancellationTokenSource = new CancellationTokenSource();

            // Hook the cancellation callback (called whenever Task.cancel is called)
            // TODO this probably could be used to notify the debug engine and others of the cancellation
            //SendCancellationTokenSource.Token.Register(() => NotifySendCancelingTask());
        }

        #region Interface implementations

        public DateTime LastActivity { get; set; }

        public async Task<uint> SendBufferAsync(byte[] buffer, TimeSpan waiTimeout, CancellationToken cancellationToken)
        {
            // device must be connected
            if (EventHandlerForSerialDevice.Current.IsDeviceConnected && !cancellationToken.IsCancellationRequested)
            {
                DataWriter outputStreamWriter = new DataWriter(EventHandlerForSerialDevice.Current.Device.OutputStream);
                Task<UInt32> storeAsyncTask;


                using (CancellationTokenSource linkedCts =
                        CancellationTokenSource.CreateLinkedTokenSource(SendCancellationTokenSource.Token, cancellationToken))
                {
                    try
                    {
                        // write buffer to device
                        outputStreamWriter.WriteBytes(buffer);

                        // Don't start any IO if the task was cancelled
                        lock (SendCancelLock)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            linkedCts.Token.ThrowIfCancellationRequested();

                            storeAsyncTask = outputStreamWriter.StoreAsync().AsTask(linkedCts.Token.AddTimeout(waiTimeout));
                        }

                        return await storeAsyncTask;
                    }
                    catch (TaskCanceledException)
                    {
                        // this is expected to happen, don't do anything with this
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SendRawBufferAsync-Serial-Exception occurred: {ex.Message}\r\n {ex.StackTrace}");
                        throw ex;
                    }
                    finally
                    {
                        // detach stream
                        outputStreamWriter?.DetachStream();
                        outputStreamWriter = null;
                    }
                }
            }
            else
            {
                throw new DeviceNotConnectedException();
            }

            return 0;
        }

        public async Task<byte[]> ReadBufferAsync(uint bytesToRead, TimeSpan waiTimeout, CancellationToken cancellationToken)
        {
            // device must be connected
            if (EventHandlerForSerialDevice.Current.IsDeviceConnected && !cancellationToken.IsCancellationRequested)
            {
                DataReader inputStreamReader = new DataReader(EventHandlerForSerialDevice.Current.Device.InputStream);
                Task<UInt32> loadAsyncTask;

                using (CancellationTokenSource linkedCts =
                        CancellationTokenSource.CreateLinkedTokenSource(ReadCancellationTokenSource.Token, cancellationToken))
                {

                    try
                    {
                        // Don't start any IO if the task was cancelled
                        lock (ReadCancelLock)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            linkedCts.Token.ThrowIfCancellationRequested();

                            loadAsyncTask = inputStreamReader.LoadAsync(bytesToRead).AsTask(linkedCts.Token.AddTimeout(waiTimeout));
                        }

                        UInt32 bytesRead = await loadAsyncTask;

                        if (bytesRead > 0)
                        {
                            byte[] readBuffer = new byte[bytesRead];
                            inputStreamReader?.ReadBytes(readBuffer);

                            return readBuffer;
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // this is expected to happen, don't do anything with it
                    }
                    catch (NullReferenceException)
                    {
                        // this is expected to happen when there is anything to read, don't do anything with it
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ReadBufferAsync-Serial-Exception occurred: {ex.Message}\r\n {ex.StackTrace}");
                        throw ex;
                    }
                    finally
                    {
                        // detach stream
                        inputStreamReader?.DetachStream();
                        inputStreamReader = null;
                    }
                }
            }
            else
            {
                throw new DeviceNotConnectedException();
            }

            // return empty byte array
            return new byte[0];
        }

        #endregion
    }
}
