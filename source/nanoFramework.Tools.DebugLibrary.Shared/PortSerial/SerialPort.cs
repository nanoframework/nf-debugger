//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.Serial;
using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Foundation;
using Windows.Storage.Streams;
using System.Collections.ObjectModel;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    public partial class SerialPort : PortBase, IPort
    {
        // dictionary with mapping between Serial device watcher and the device ID
        private Dictionary<DeviceWatcher, string> mapDeviceWatchersToDeviceSelector;

        // Serial device watchers suspended flag
        private bool watchersSuspended = false;

        // Serial device watchers started flag
        private bool watchersStarted = false;

        // counter of device watchers completed
        private int deviceWatchersCompletedCount = 0;

        private static SemaphoreSlim semaphore;

        private DataReader inputStreamReader;
        private DataWriter outputStreamWriter;

        /// <summary>
        /// Internal list with the actual nF Serial devices
        /// </summary>
        List<SerialDeviceInformation> SerialDevices;

        /// <summary>
        /// Internal list of the tentative devices to be checked as valid nanoFramework devices
        /// </summary>
        private List<NanoDeviceBase> tentativeNanoFrameworkDevices = new List<NanoDeviceBase>();

        /// <summary>
        /// Creates an Serial debug client
        /// </summary>
        public SerialPort(object callerApp)
        {
            mapDeviceWatchersToDeviceSelector = new Dictionary<DeviceWatcher, String>();
            NanoFrameworkDevices = new ObservableCollection<NanoDeviceBase>();
            SerialDevices = new List<Serial.SerialDeviceInformation>();

            // set caller app property, if any
            if (callerApp != null)
            {

#if WINDOWS_UWP
                EventHandlerForSerialDevice.CallerApp = callerApp as Windows.UI.Xaml.Application;
#else
                EventHandlerForSerialDevice.CallerApp = callerApp as System.Windows.Application;
#endif
            };

            // init semaphore
            semaphore = new SemaphoreSlim(1, 1);

            Task.Factory.StartNew(() => {
                StartSerialDeviceWatchers();
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

            mapDeviceWatchersToDeviceSelector.Add(deviceWatcher, deviceSelector);
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
            watchersStarted = true;
            deviceWatchersCompletedCount = 0;
            IsDevicesEnumerationComplete = false;

            foreach (DeviceWatcher deviceWatcher in mapDeviceWatchersToDeviceSelector.Keys)
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
            if (watchersStarted)
            {
                watchersSuspended = true;
                StopDeviceWatchers();
            }
            else
            {
                watchersSuspended = false;
            }
        }

        /// <summary>
        /// Should be called on host app OnAppResume() event to properly handle that status.
        /// See AppSuspending for why we are starting the device watchers again.
        /// </summary>
        public void AppResumed()
        {
            if (watchersSuspended)
            {
                watchersSuspended = false;
                StartDeviceWatchers();
            }
        }

        /// <summary>
        /// Stops all device watchers.
        /// </summary>
        private void StopDeviceWatchers()
        {
            // Stop all device watchers
            foreach (DeviceWatcher deviceWatcher in mapDeviceWatchersToDeviceSelector.Keys)
            {
                if ((deviceWatcher.Status == DeviceWatcherStatus.Started)
                    || (deviceWatcher.Status == DeviceWatcherStatus.EnumerationCompleted))
                {
                    deviceWatcher.Stop();
                }
            }

            // Clear the list of devices so we don't have potentially disconnected devices around
            ClearDeviceEntries();

            watchersStarted = false;
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
            // search the device list for a device with a matching interface ID
            var serialMatch = FindDevice(deviceInformation.Id);

            // Add the device if it's new
            if (serialMatch == null)
            {
                SerialDevices.Add(new SerialDeviceInformation(deviceInformation, deviceSelector));

                // search the NanoFramework device list for a device with a matching interface ID
                var nanoFrameworkDeviceMatch = FindNanoFrameworkDevice(deviceInformation.Id);

                if (nanoFrameworkDeviceMatch == null)
                {
                    //     Create a new element for this device interface, and queue up the query of its
                    //     device information

                    var newNanoFrameworkDevice = new NanoDevice<NanoSerialDevice>();
                    newNanoFrameworkDevice.Device.DeviceInformation = new SerialDeviceInformation(deviceInformation, deviceSelector);
                    newNanoFrameworkDevice.Parent = this;
                    newNanoFrameworkDevice.DebugEngine = new Engine(this, newNanoFrameworkDevice);
                    newNanoFrameworkDevice.Transport = TransportType.Serial;

                    // perform check for valid nanoFramework device is this is not the initial enumeration
                    if (IsDevicesEnumerationComplete)
                    {
                        // try opening the device to check for a valid nanoFramework device
                        if (await ConnectSerialDeviceAsync(newNanoFrameworkDevice.Device.DeviceInformation))
                        {
                            Debug.WriteLine("New Serial device: " + deviceInformation.Id);

                            if (await CheckValidNanoFrameworkSerialDeviceAsync())
                            {
                                // done here, close the device
                                EventHandlerForSerialDevice.Current.CloseDevice();

                                //add device to the collection
                                NanoFrameworkDevices.Add(newNanoFrameworkDevice as NanoDeviceBase);
                                Debug.WriteLine($"New Serial device: {newNanoFrameworkDevice.Description} {(((NanoDevice<NanoSerialDevice>)newNanoFrameworkDevice).Device.DeviceInformation.DeviceInformation.Id)}");

                                return;
                            }
                        }

                        Debug.WriteLine($"Removing { deviceInformation.Id } from collection...");

                        // can't do anything with this one, better dispose it
                        newNanoFrameworkDevice.Dispose();
                    }
                    else
                    {
                        tentativeNanoFrameworkDevices.Add(newNanoFrameworkDevice as NanoDeviceBase);
                    }
                }
            }
        }

        private void RemoveDeviceFromList(string deviceId)
        {
            // Removes the device entry from the internal list; therefore the UI
            var deviceEntry = FindDevice(deviceId);

            Debug.WriteLine("Serial device removed: " + deviceId);

            SerialDevices.Remove(deviceEntry);

            // get device
            var device = FindNanoFrameworkDevice(deviceId);
            // yes, remove it from collection
            NanoFrameworkDevices.Remove(device);

            device = null;
        }

        private void ClearDeviceEntries()
        {
            SerialDevices.Clear();
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
                foreach (SerialDeviceInformation entry in SerialDevices)
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
                    return tentativeNanoFrameworkDevices.FirstOrDefault(d => ((d as NanoDevice<NanoSerialDevice>).Device.DeviceInformation as SerialDeviceInformation).DeviceInformation.Id == deviceId);
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
            AddDeviceToList(deviceInformation, mapDeviceWatchersToDeviceSelector[sender]);
        }

        #endregion


        #region Handlers and events for Device Enumeration Complete 

        private void OnDeviceEnumerationComplete(DeviceWatcher sender, object args)
        {
            // add another device watcher completed
            deviceWatchersCompletedCount++;

            if (deviceWatchersCompletedCount == mapDeviceWatchersToDeviceSelector.Count)
            {
                // prepare a list of devices that are to be removed if they are deemed as not valid nanoFramework devices
                var devicesToRemove = new List<NanoDeviceBase>();

                foreach (NanoDeviceBase device in tentativeNanoFrameworkDevices)
                {
                    // connect to the device (as Task to get rid of the await)
                    var connectTask = ConnectDeviceAsync(device);

                    if (connectTask.Result)
                    {

                        var checkValidNFDeviceTask = CheckValidNanoFrameworkSerialDeviceAsync();

                        if (checkValidNFDeviceTask.Result)
                        {
                            Debug.WriteLine($"New Serial device: {device.Description} {(((NanoDevice<NanoSerialDevice>)device).Device.DeviceInformation.DeviceInformation.Id)}");
                            NanoFrameworkDevices.Add(device);
                        }

                        // done here, disconnect from the device now
                        ((NanoDevice<NanoSerialDevice>)device).Disconnect();
                    }
                    else
                    {
                        // couldn't open device
                    }
                }

                // all watchers have completed enumeration
                IsDevicesEnumerationComplete = true;

                Debug.WriteLine($"Serial device enumeration completed. Found {NanoFrameworkDevices.Count} devices");

                // fire event that Serial enumeration is complete 
                OnDeviceEnumerationCompleted();
            }
        }

        private async Task<bool> CheckValidNanoFrameworkSerialDeviceAsync()
        {
            // get name
            var name = EventHandlerForSerialDevice.Current.DeviceInformation?.Properties["System.ItemNameDisplay"] as string;

            // try get serial number
            var serialNumber = EventHandlerForSerialDevice.Current.DeviceInformation.GetSerialNumber();

            // acceptable names and that are know valid nanoFramework devices
            if (
                // STM32 COM port on on-board ST Link found in most NUCLEO boards
                (name == "STM32 STLink")
               )
            {
                // need an extra check on this because this can be 'just' a regular COM port without any nanoFramework device behind

                // fill in description for this device
                var device = FindNanoFrameworkDevice(EventHandlerForSerialDevice.Current.DeviceInformation.Id);

                // need an extra check on this because this can be 'just' a regular COM port without any nanoFramework device behind
                var connectionResult = await device.DebugEngine.ConnectAsync(1, 1000, true);

                if (connectionResult)
                {
                    // should be a valid nanoFramework device
                    device.Description = name + " @ " + EventHandlerForSerialDevice.Current.Device.PortName;

                    // disconnect now
                    device.DebugEngine.Disconnect();

                    // done here
                    return true;
                }
                else
                {
                    // doesn't look like a nanoFramework device
                    return false;
                }

            }
            else if (serialNumber != null)
            {
                if (serialNumber.Contains("NANO_"))
                {
                    var device = FindNanoFrameworkDevice(EventHandlerForSerialDevice.Current.DeviceInformation.Id);
                    device.Description = serialNumber + " @ " + EventHandlerForSerialDevice.Current.Device.PortName;

                    // should be a valid nanoFramework device, done here
                    return true;
                }
            }

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


        public Task<bool> ConnectDeviceAsync(NanoDeviceBase device)
        {
            inputStreamReader = null;
            outputStreamWriter = null;

            return ConnectSerialDeviceAsync((device as NanoDevice<NanoSerialDevice>).Device.DeviceInformation as SerialDeviceInformation);
        }

        private Task<bool> ConnectSerialDeviceAsync(SerialDeviceInformation serialDeviceInfo)
        {
            // try to determine if we already have this device opened.
            if (EventHandlerForSerialDevice.Current != null)
            {
                // device matches
                if (EventHandlerForSerialDevice.Current.DeviceInformation == serialDeviceInfo.DeviceInformation)
                {
                    return Task.FromResult(true);
                }
            }

            inputStreamReader = null;
            outputStreamWriter = null;

            // access the Current in EventHandlerForDevice to create a watcher for the device we are connecting to
            var isConnected = EventHandlerForSerialDevice.Current.IsDeviceConnected;

            return EventHandlerForSerialDevice.Current.OpenDeviceAsync(serialDeviceInfo.DeviceInformation, serialDeviceInfo.DeviceSelector);
        }

        public void DisconnectDevice(NanoDeviceBase device)
        {
            if (FindDevice(((device as NanoDevice<NanoSerialDevice>).Device.DeviceInformation as SerialDeviceInformation).DeviceInformation.Id) != null)
            {
                EventHandlerForSerialDevice.Current.CloseDevice();

                inputStreamReader?.DetachStream();
                inputStreamReader?.DetachBuffer();
                inputStreamReader?.Dispose();

                outputStreamWriter?.DetachStream();
                outputStreamWriter?.DetachBuffer();
                outputStreamWriter?.Dispose();
            }
        }

        #region Interface implementations

        public DateTime LastActivity { get; set; }

        public void DisconnectDevice(SerialDevice device)
        {
            EventHandlerForSerialDevice.Current.CloseDevice();
        }

        public async Task<uint> SendBufferAsync(byte[] buffer, TimeSpan waiTimeout, CancellationToken cancellationToken)
        {
            uint bytesWritten = 0;

            // device must be connected
            if (EventHandlerForSerialDevice.Current.IsDeviceConnected)
            {
                // create a stream writer with serial device OutputStream, if there isn't one already
                if (outputStreamWriter == null)
                {
                    outputStreamWriter = new DataWriter(EventHandlerForSerialDevice.Current.Device.OutputStream);
                }

                // serial works as a "single channel" so we can only TX or RX, 
                // meaning that access to the resource has to be protected with a semaphore
                var semaphoreEntered = await semaphore.WaitAsync(1000, cancellationToken);

                if (semaphoreEntered)
                {
                    try
                    {
                        // write buffer to device
                        outputStreamWriter.WriteBytes(buffer);

                        // need to have a timeout to cancel the read task otherwise it may end up waiting forever for this to return
                        // because we have an external cancellation token and the above timeout cancellation token, need to combine both
                        Task<uint> storeAsyncTask = outputStreamWriter.StoreAsync().AsTask(cancellationToken.AddTimeout(waiTimeout));

                        bytesWritten = await storeAsyncTask;

                        if (bytesWritten > 0)
                        {
                            LastActivity = DateTime.Now;
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // this is expected to happen, don't do anything with this
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SendRawBufferAsync-Serial-Exception occurred: {ex.Message}\r\n {ex.StackTrace}");
                    }
                    finally
                    {
                        semaphore.Release();
                        outputStreamWriter?.DetachBuffer();
                    }
                }
            }
            else
            {
                // NotifyDeviceNotConnected
                Debug.WriteLine("NotifyDeviceNotConnected");
            }

            return bytesWritten;
        }

        public async Task<byte[]> ReadBufferAsync(uint bytesToRead, TimeSpan waiTimeout, CancellationToken cancellationToken)
        {
            // device must be connected
            if (EventHandlerForSerialDevice.Current.IsDeviceConnected)
            {
                // serial works as a "single channel" so we can only TX or RX, 
                // meaning that access to the resource has to be protected with a semaphore
                var semaphoreEntered = await semaphore.WaitAsync(1000, cancellationToken);

                if (semaphoreEntered)
                {
                    // create a stream reader with serial device InputStream, if there isn't one already
                    if (inputStreamReader == null)
                    {
                        inputStreamReader = new DataReader(EventHandlerForSerialDevice.Current.Device.InputStream);
                    }

                    try
                    {
                        // need to have a timeout to cancel the read task otherwise it may end up waiting forever for this to return
                        // because we have an external cancellation token and the above timeout cancellation token, need to combine both

                        // get how many bytes are available to read
                        uint bytesRead = await inputStreamReader.LoadAsync(bytesToRead).AsTask(cancellationToken.AddTimeout(waiTimeout)).ConfigureAwait(true);

                        byte[] readBuffer = new byte[bytesToRead];
                        inputStreamReader.ReadBytes(readBuffer);

                        return readBuffer;
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
                        Debug.WriteLine($"SendRawBufferAsync-Serial-Exception occurred: {ex.Message}\r\n {ex.StackTrace}");
                    }
                    finally
                    {
                        // release serial device semaphore
                        semaphore.Release();

                        // detach read buffer
                        inputStreamReader.DetachBuffer();
                    }
                }
            }
            else
            {
                // FIXME 
                // NotifyDeviceNotConnected
                Debug.WriteLine("NotifyDeviceNotConnected");
            }

            // return empty byte array
            return new byte[0];
        }

        #endregion
    }
}
