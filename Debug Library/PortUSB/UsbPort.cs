//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.Usb;
using Windows.Foundation;
using Windows.Storage.Streams;
using Windows.UI.Xaml;

namespace nanoFramework.Tools.Debugger.Usb
{
    public class UsbPort : PortBase, IPort
    {
        // dictionary with mapping between USB device watcher and the device ID
        private Dictionary<DeviceWatcher, string> mapDeviceWatchersToDeviceSelector;

        // USB device watchers suspended flag
        private bool watchersSuspended = false;

        // USB device watchers started flag
        private bool watchersStarted = false;

        // counter of device watchers completed
        private int deviceWatchersCompletedCount = 0;
        private bool isAllDevicesEnumerated = false;

        private object cancelIoLock = new object();
        private static SemaphoreSlim semaphore;

        /// <summary>
        /// Internal list with the actual nF USB devices
        /// </summary>
        List<UsbDeviceInformation> UsbDevices;

        /// <summary>
        /// Creates an USB debug client
        /// </summary>
        public UsbPort(Application callerApp)
        {
            mapDeviceWatchersToDeviceSelector = new Dictionary<DeviceWatcher, String>();
            NanoFrameworkDevices = new ObservableCollection<NanoDeviceBase>();
            UsbDevices = new List<UsbDeviceInformation>();

            // set caller app property
            EventHandlerForUsbDevice.CallerApp = callerApp;

            // init semaphore
            semaphore = new SemaphoreSlim(1, 1);

            Task.Factory.StartNew(() =>
            {
                StartUsbDeviceWatchers();
            });
        }

        #region Device watchers initialization

        /*////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
        Add a device watcher initialization method for each supported device that should be watched.
        That initialization method must be called from the InitializeDeviceWatchers() method above so the watcher is actually started.
        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////*/

        /// <summary>
        /// Initialize the device watcher for the ST Discovery4
        /// </summary>
        private void InitializeStDiscovery4DeviceWatcher()
        {
            // better use  most specific type of DeviceSelector: VID, PID and class GUID
            var stDiscovery4Selector = Windows.Devices.Usb.UsbDevice.GetDeviceSelector(STM_Discovery4.DeviceVid, STM_Discovery4.DevicePid, STM_Discovery4.DeviceInterfaceClass);

            // Create a device watcher to look for instances of this device
            var stDiscovery4Watcher = DeviceInformation.CreateWatcher(stDiscovery4Selector);

            // Allow the EventHandlerForDevice to handle device watcher events that relates or effects this device (i.e. device removal, addition, app suspension/resume)
            AddDeviceWatcher(stDiscovery4Watcher, stDiscovery4Selector);
        }

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
            // ST Discovery 4
            InitializeStDiscovery4DeviceWatcher();
        }

        public void StartUsbDeviceWatchers()
        {
            // Initialize the USB device watchers to be notified when devices are connected/removed
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
            isAllDevicesEnumerated = false;

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
            var usbMatch = FindDevice(deviceInformation.Id);

            Debug.WriteLine("New USB device: " + deviceInformation.Id);

            // Add the device if it's new
            if (usbMatch == null)
            {
                UsbDevices.Add(new UsbDeviceInformation(deviceInformation, deviceSelector));

                // search the NanoFramework device list for a device with a matching interface ID
                var nanoFrameworkDeviceMatch = FindNanoFrameworkDevice(deviceInformation.Id);

                if (nanoFrameworkDeviceMatch == null)
                {
                    //     Create a new element for this device interface, and queue up the query of its
                    //     device information

                    var newNanoFrameworkDevice = new NanoDevice<NanoUsbDevice>();
                    //newMFDevice.DeviceInformation = new UsbDeviceInformation(deviceInformation, deviceSelector);
                    newNanoFrameworkDevice.Device.DeviceInformation = new UsbDeviceInformation(deviceInformation, deviceSelector);
                    newNanoFrameworkDevice.Parent = this;
                    newNanoFrameworkDevice.DebugEngine = new Engine(this, newNanoFrameworkDevice);
                    newNanoFrameworkDevice.Transport = TransportType.Usb;

                    // Add the new element to the end of the list of devices
                    NanoFrameworkDevices.Add(newNanoFrameworkDevice as NanoDeviceBase);

                    // now fill in the description
                    // try opening the device to read the descriptor
                    if (await ConnectUsbDeviceAsync(newNanoFrameworkDevice.Device.DeviceInformation).ConfigureAwait(false))
                    {
                        // the device description format is kept to maintain backwards compatibility
                        newNanoFrameworkDevice.Description = EventHandlerForUsbDevice.Current.DeviceInformation.Name + "_" + await GetDeviceDescriptor(5).ConfigureAwait(false);

                        Debug.WriteLine("Add new nanoFramework device to list: " + newNanoFrameworkDevice.Description + " @ " + newNanoFrameworkDevice.Device.DeviceInformation.DeviceSelector);

                        // done here, close device
                        EventHandlerForUsbDevice.Current.CloseDevice();

                    }
                    else
                    {
                        // couldn't open device, better remove it from the lists
                        NanoFrameworkDevices.Remove(newNanoFrameworkDevice as NanoDeviceBase);
                        UsbDevices.Remove(newNanoFrameworkDevice.Device.DeviceInformation);
                    }
                }
                else
                {
                    // this NanoFramework device is already on the list
                    // stop the dispose countdown!
                    nanoFrameworkDeviceMatch.StopCountdownForDispose();

                    // set port parent
                    //(mfDeviceMatch as MFDevice<MFUsbDevice>).Device.Parent = this;
                    //// instantiate a debug engine
                    //(mfDeviceMatch as MFDevice<MFUsbDevice>).Device.DebugEngine = new Engine(this, (mfDeviceMatch as MFDevice<MFUsbDevice>));
                }
            }
        }

        private void RemoveDeviceFromList(string deviceId)
        {
            // Removes the device entry from the internal list; therefore the UI
            var deviceEntry = FindDevice(deviceId);

            Debug.WriteLine("USB device removed: " + deviceId);

            UsbDevices.Remove(deviceEntry);

            // start thread to dispose and remove device from collection if it doesn't enumerate again in 2 seconds
            Task.Factory.StartNew(() =>
            {
                // get device
                var device = FindNanoFrameworkDevice(deviceId);

                if (device != null)
                {
                    // set device to dispose if it doesn't come back in 2 seconds
                    device.StartCountdownForDispose();

                    // hold here for the same time as the default timer of the dispose timer
                    new ManualResetEvent(false).WaitOne(TimeSpan.FromSeconds(2.5));

                    // check is object was disposed
                    if((device as NanoDevice<NanoUsbDevice>).KillFlag)
                    {
                        // yes, remove it from collection
                        NanoFrameworkDevices.Remove(device);

                        Debug.WriteLine("Removing device " + device.Description);

                        device = null;
                    }
                }
            });
        }

        private void ClearDeviceEntries()
        {
            UsbDevices.Clear();
        }

        /// <summary>
        /// Searches through the existing list of devices for the first DeviceListEntry that has
        /// the specified device Id.
        /// </summary>
        /// <param name="deviceId">Id of the device that is being searched for</param>
        /// <returns>DeviceListEntry that has the provided Id; else a nullptr</returns>
        private UsbDeviceInformation FindDevice(String deviceId)
        {
            if (deviceId != null)
            {
                foreach (UsbDeviceInformation entry in UsbDevices)
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

                // usbMatch.Device.DeviceInformation
                return NanoFrameworkDevices.FirstOrDefault(d => ((d as NanoDevice<NanoUsbDevice>).Device.DeviceInformation as UsbDeviceInformation).DeviceInformation.Id == deviceId);
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
                Debug.WriteLine($"USB device enumeration completed. Found { UsbDevices.Count } devices");

                // all watchers have completed enumeration
                isAllDevicesEnumerated = true;

                // fire event that USB enumeration is complete 
                OnDeviceEnumerationCompleted();
            }
        }

        private async Task<string> GetDeviceDescriptor(int index)
        {
            try
            {
                // maximum expected length of descriptor
                uint readBufferSize = 64;

                // prepare buffer to hold the descriptor data returned from the device
                var buffer = new Windows.Storage.Streams.Buffer(readBufferSize);

                // setup packet to perform the request
                UsbSetupPacket setupPacket = new UsbSetupPacket
                {
                    RequestType = new UsbControlRequestType
                    {
                        Direction = UsbTransferDirection.In,
                        Recipient = UsbControlRecipient.SpecifiedInterface,
                        ControlTransferType = UsbControlTransferType.Vendor,
                    },
                    // request to get a descriptor
                    Request = (byte)UsbDeviceRequestType.GetDescriptor,

                    // descriptor number to be read
                    Value = (uint)index,

                    // max length of response
                    Length = readBufferSize
                };

                // send control to device
                IBuffer responseBuffer = await EventHandlerForUsbDevice.Current.Device.SendControlInTransferAsync(setupPacket, buffer);

                // read from a buffer with a data reader
                DataReader reader = DataReader.FromBuffer(responseBuffer);

                // USB data is Little Endian 
                reader.ByteOrder = ByteOrder.LittleEndian;

                // set encoding to UTF16 & Little Endian
                reader.UnicodeEncoding = Windows.Storage.Streams.UnicodeEncoding.Utf16LE;

                // read 1st byte (descriptor length)
                int descriptorLenght = reader.ReadByte();
                // read 2nd byte (descriptor type)
                int descryptorType = reader.ReadByte();

                // check if this a string (string descriptor type is 0x03)
                if (descryptorType == 0x03)
                {
                    // read a string with remaining bytes available
                    // the string length is half the available bytes because it's UTF16 encoded (2 bytes for each char)
                    return reader.ReadString(reader.UnconsumedBufferLength / 2);
                }
            }
            catch (Exception)
            {

            }

            // if we get here something went wrong above, so we don't have a descriptor
            return string.Empty;
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
            return await ConnectUsbDeviceAsync((device as NanoDevice<NanoUsbDevice>).Device.DeviceInformation as UsbDeviceInformation);
        }

        private async Task<bool> ConnectUsbDeviceAsync(UsbDeviceInformation usbDeviceInfo)
        {
            // try to determine if we already have this device opened.
            if (EventHandlerForUsbDevice.Current != null)
            {
                // device matches
                if (EventHandlerForUsbDevice.Current.DeviceInformation == usbDeviceInfo.DeviceInformation)
                {
                    return true;
                }
            }

            // Create an EventHandlerForDevice to watch for the device we are connecting to
            EventHandlerForUsbDevice.CreateNewEventHandlerForDevice();

            return await EventHandlerForUsbDevice.Current.OpenDeviceAsync(usbDeviceInfo.DeviceInformation, usbDeviceInfo.DeviceSelector).ConfigureAwait(false);
        }

        public void DisconnectDevice(NanoDeviceBase device)
        {
            if (FindDevice(((device as NanoDevice<NanoUsbDevice>).Device.DeviceInformation as UsbDeviceInformation).DeviceInformation.Id) != null)
            {
                EventHandlerForUsbDevice.Current.CloseDevice();
            }
        }

        #region Interface implementations

        public DateTime LastActivity { get; set; }

        public void DisconnectDevice(UsbDevice device)
        {
            EventHandlerForUsbDevice.Current.CloseDevice();
        }

        public async Task<uint> SendBufferAsync(byte[] buffer, TimeSpan waiTimeout, CancellationToken cancellationToken)
        {
            uint bytesWritten = 0;

            // device must be connected
            if (EventHandlerForUsbDevice.Current.IsDeviceConnected)
            {
                //sanity check for available OUT pipes (must have at least one!)
                if (EventHandlerForUsbDevice.Current.Device.DefaultInterface.BulkOutPipes.Count < 1)
                {
                    // FIXME
                    // throw exception?
                }

                // gets the 1st OUT stream for the device
                var stream = EventHandlerForUsbDevice.Current.Device.DefaultInterface.BulkOutPipes[(int)0].OutputStream;

                // create a data writer to access the device OUT stream
                var writer = new DataWriter(stream);

                // write buffer
                writer.WriteBytes(buffer);

                // need to have a timeout to cancel the read task otherwise it may end up waiting forever for this to return
                var timeoutCancelatioToken = new CancellationTokenSource(waiTimeout).Token;

                // because we have an external cancellation token and the above timeout cancellation token, need to combine both
                var linkedCancelationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancelatioToken).Token;

                Task<uint> storeAsyncTask;

                try
                {

                    // Don't start any IO if the task has been canceled
                    lock (cancelIoLock)
                    {
                        // set this makes sure that an exception is thrown when the cancellation token is set
                        linkedCancelationToken.ThrowIfCancellationRequested();

                        // Now the buffer data is actually flushed out to the device.
                        // We should implement a cancellation Token here so we are able to stop the task operation explicitly if needed
                        // The completion function should still be called so that we can properly handle a canceled task
                        storeAsyncTask = writer.StoreAsync().AsTask(linkedCancelationToken);
                    }

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
            }
            else
            {
                // FIXME 
                // NotifyDeviceNotConnected
            }

            return bytesWritten;
        }

        public async Task<DataReader> ReadBufferAsync(uint bytesToRead, TimeSpan waiTimeout, CancellationToken cancellationToken)
        {
            // device must be connected
            if (EventHandlerForUsbDevice.Current.IsDeviceConnected)
            {
                //sanity check for available IN pipes (must have at least one!)
                if (EventHandlerForUsbDevice.Current.Device.DefaultInterface.BulkInPipes.Count < 1)
                {
                    // FIXME
                    // throw exception?
                }

                // gets the 1st IN stream for the device
                var stream = EventHandlerForUsbDevice.Current.Device.DefaultInterface.BulkInPipes[0].InputStream;

                DataReader reader = new DataReader(stream);
                uint bytesRead = 0;

                // need to have a timeout to cancel the read task otherwise it may end up waiting forever for this to return
                var timeoutCancelatioToken = new CancellationTokenSource(waiTimeout).Token;

                // because we have an external cancellation token and the above timeout cancellation token, need to combine both
                var linkedCancelationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancelatioToken).Token;

                Task<UInt32> loadAsyncTask;

                //Debug.WriteLine("### waiting");
                await semaphore.WaitAsync();
                //Debug.WriteLine("### got it");

                try
                {
                    // Don't start any IO if the task has been canceled
                    lock (cancelIoLock)
                    {
                        // set this makes sure that an exception is thrown when the cancellation token is set
                        linkedCancelationToken.ThrowIfCancellationRequested();

                        // We should implement a cancellation Token here so we are able to stop the task operation explicitly if needed
                        // The completion function should still be called so that we can properly handle a canceled task
                        loadAsyncTask = reader.LoadAsync(bytesToRead).AsTask(linkedCancelationToken);
                    }

                    bytesRead = await loadAsyncTask;
                }
                catch (TaskCanceledException)
                {
                    // this is expected to happen, don't do anything with this 
                }
                finally
                {
                    semaphore.Release();
                    //Debug.WriteLine("### released");
                }

                return reader;
            }
            else
            {
                // FIXME 
                // NotifyDeviceNotConnected
            }

            return null;
        }

        #endregion

    }
}
