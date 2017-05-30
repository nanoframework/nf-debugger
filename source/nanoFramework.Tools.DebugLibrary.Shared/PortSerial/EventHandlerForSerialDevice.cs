//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Foundation;

namespace nanoFramework.Tools.Debugger.Serial
{
    /// <summary>
    /// This class handles the required changes and operation of an SerialDevice when a specific app event
    /// is raised (app suspension and resume) or when the device is disconnected. The device watcher events are also handled here.
    /// </summary>
    public partial class EventHandlerForSerialDevice
    {
        /// <summary>
        /// Allows for singleton EventHandlerForSerialEclo
        /// </summary>
        private static volatile EventHandlerForSerialDevice eventHandlerForNanoFrameworkDevice;

        /// <summary>
        /// Used to synchronize threads to avoid multiple instantiations of eventHandlerForSerialEclo.
        /// </summary>
        private static object singletonCreationLock = new object();

        private string deviceSelector;
        private DeviceWatcher deviceWatcher;

        private DeviceInformation deviceInformation;
        private DeviceAccessInformation deviceAccessInformation;
        private SerialDevice device;

        private TypedEventHandler<EventHandlerForSerialDevice, DeviceInformation> deviceCloseCallback;
        private TypedEventHandler<EventHandlerForSerialDevice, DeviceInformation> deviceConnectedCallback;

        private TypedEventHandler<DeviceWatcher, DeviceInformation> deviceAddedEventHandler;
        private TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> deviceRemovedEventHandler;
        private TypedEventHandler<DeviceAccessInformation, DeviceAccessChangedEventArgs> deviceAccessEventHandler;

        private bool watcherSuspended;
        private bool watcherStarted;

        private bool isBackgroundTask;
        private bool isEnabledAutoReconnect;

        /// <summary>
        /// Enforces the singleton pattern so that there is only one object handling app events
        /// as it relates to the SerialDevice because this app only supports communicating with one device at a time. 
        ///
        /// An instance of EventHandlerForSerialUsb is globally available because the device needs to persist across scenario pages.
        ///
        /// If there is no instance of EventHandlerForSerialUsb created before this property is called,
        /// an EventHandlerForSerialUsb will be created; the EventHandlerForSerialUsb created this way
        /// is not meant for BackgroundTasks.
        /// </summary>
        public static EventHandlerForSerialDevice Current
        {
            get
            {
                lock (singletonCreationLock)
                {
                    if (eventHandlerForNanoFrameworkDevice == null)
                    {
                        CreateNewEventHandlerForDevice();
                    }
                }

                return eventHandlerForNanoFrameworkDevice;
            }
        }

        /// <summary>
        /// Creates a new instance of EventHandlerForSerialEclo, enables auto reconnect, and uses it as the Current instance.
        /// </summary>
        public static void CreateNewEventHandlerForDevice()
        {
            eventHandlerForNanoFrameworkDevice = new EventHandlerForSerialDevice(false);
        }

        /// <summary>
        /// Creates a new instance of EventHandlerForSerialEclo, disables auto reconnect, and uses it as the Current instance.
        /// Background tasks do not need to worry about app events, so we will not be registering for app events
        /// </summary>
        public static void CreateNewEventHandlerForDeviceForBackgroundTasks()
        {
            eventHandlerForNanoFrameworkDevice = new EventHandlerForSerialDevice(true);
        }


        public TypedEventHandler<EventHandlerForSerialDevice, DeviceInformation> OnDeviceClose
        {
            get
            {
                return deviceCloseCallback;
            }

            set
            {
                deviceCloseCallback = value;
            }
        }

        public TypedEventHandler<EventHandlerForSerialDevice, DeviceInformation> OnDeviceConnected
        {
            get
            {
                return deviceConnectedCallback;
            }

            set
            {
                deviceConnectedCallback = value;
            }
        }

        public bool IsDeviceConnected
        {
            get
            {
                return (device != null);
            }
        }

        public SerialDevice Device
        {
            get
            {
                return device;
            }
        }

        /// <summary>
        /// This DeviceInformation represents which device is connected or which device will be reconnected when
        /// the device is plugged in again (if IsEnabledAutoReconnect is true);.
        /// </summary>
        public DeviceInformation DeviceInformation
        {
            get
            {
                return deviceInformation;
            }
        }

        /// <summary>
        /// Returns DeviceAccessInformation for the device that is currently connected using this EventHandlerForSerialEclo
        /// object.
        /// </summary>
        public DeviceAccessInformation DeviceAccessInformation
        {
            get
            {
                return deviceAccessInformation;
            }
        }

        /// <summary>
        /// DeviceSelector AQS used to find this device
        /// </summary>
        public String DeviceSelector
        {
            get
            {
                return deviceSelector;
            }
        }

        /// <summary>
        /// True if EventHandlerForSerialEclo will attempt to reconnect to the device once it is plugged into the computer again
        /// </summary>
        public bool IsEnabledAutoReconnect
        {
            get
            {
                return isEnabledAutoReconnect;
            }
            set
            {
                isEnabledAutoReconnect = value;
            }
        }

        /// <summary>
        /// This method opens the device using the WinRT Serial API. After the device is opened, save the device
        /// so that it can be used across scenarios.
        ///
        /// It is important that the FromIdAsync call is made on the UI thread because the consent prompt can only be displayed
        /// on the UI thread.
        /// 
        /// This method is used to reopen the device after the device reconnects to the computer and when the app resumes.
        /// </summary>
        /// <param name="deviceInfo">Device information of the device to be opened</param>
        /// <param name="deviceSelector">The AQS used to find this device</param>
        /// <returns>True if the device was successfully opened, false if the device could not be opened for well known reasons.
        /// An exception may be thrown if the device could not be opened for extraordinary reasons.</returns>
        public async Task<bool> OpenDeviceAsync(DeviceInformation deviceInfo, string deviceSelector)
        {
            device = await SerialDevice.FromIdAsync(deviceInfo.Id);

            bool successfullyOpenedDevice = false;

            try
            {
                // Device could have been blocked by user or the device has already been opened by another app.
                if (device != null)
                {
                    successfullyOpenedDevice = true;

                    deviceInformation = deviceInfo;
                    this.deviceSelector = deviceSelector;

                    //Debug.WriteLine($"Device {deviceInformation.Id} opened");

                    // adjust settings for serial port
                    device.BaudRate = 115200;
                    
                    /////////////////////////////////////////////////////////////
                    // need to FORCE the parity setting to _NONE_ because        
                    // the default on the current ST Link is different causing 
                    // the communication to fail
                    /////////////////////////////////////////////////////////////
                    device.Parity = SerialParity.None;

                    device.WriteTimeout = TimeSpan.FromSeconds(1);
                    device.ReadTimeout = TimeSpan.FromSeconds(2);

                    // Notify registered callback handle that the device has been opened
                    deviceConnectedCallback?.Invoke(this, deviceInformation);

                    // Background tasks are not part of the app, so app events will not have an affect on the device
                    if (!isBackgroundTask && (appSuspendEventHandler == null || appResumeEventHandler == null))
                    {
                        RegisterForAppEvents();
                    }

                    // User can block the device after it has been opened in the Settings charm. We can detect this by registering for the 
                    // DeviceAccessInformation.AccessChanged event
                    if (deviceAccessEventHandler == null)
                    {
                        RegisterForDeviceAccessStatusChange();
                    }

                    // Create and register device watcher events for the device to be opened unless we're reopening the device
                    if (deviceWatcher == null)
                    {
                        deviceWatcher = DeviceInformation.CreateWatcher(deviceSelector);

                        RegisterForDeviceWatcherEvents();
                    }

                    if (!watcherStarted)
                    {
                        // Start the device watcher after we made sure that the device is opened.
                        StartDeviceWatcher();
                    }
                }
                else
                {
                    successfullyOpenedDevice = false;

                    //notificationStatus = NotifyType.ErrorMessage;

                    var deviceAccessStatus = DeviceAccessInformation.CreateFromId(deviceInfo.Id).CurrentStatus;

                    switch (deviceAccessStatus)
                    {
                        case DeviceAccessStatus.DeniedByUser:
                            //Debug.WriteLine($"Access to the device was blocked by the user : {deviceInfo.Id}");
                            break;

                        case DeviceAccessStatus.DeniedBySystem:
                            // This status is most likely caused by app permissions (did not declare the device in the app's package.appxmanifest)
                            // This status does not cover the case where the device is already opened by another app.
                            //Debug.WriteLine($"Access to the device was blocked by the system : {deviceInfo.Id}");
                            break;

                        default:
                            // Most likely the device is opened by another app, but cannot be sure
                            //Debug.WriteLine($"Unknown error, possibly opened by another app : {deviceInfo.Id}");
                            break;
                    }
                }
            }
            // catch all because the device open might fail for a number of reasons
            catch { }

            return successfullyOpenedDevice;
        }

        /// <summary>
        /// Closes the device, stops the device watcher, stops listening for app events, and resets object state to before a device
        /// was ever connected.
        /// </summary>
        public void CloseDevice()
        {
            if (IsDeviceConnected)
            {
                CloseCurrentlyConnectedDevice();
            }

            if (deviceWatcher != null)
            {
                if (watcherStarted)
                {
                    StopDeviceWatcher();

                    UnregisterFromDeviceWatcherEvents();
                }

                deviceWatcher = null;
            }

            if (deviceAccessInformation != null)
            {
                UnregisterFromDeviceAccessStatusChange();

                deviceAccessInformation = null;
            }

            if (appSuspendEventHandler != null || appResumeEventHandler != null)
            {
                UnregisterFromAppEvents();
            }

            deviceInformation = null;
            deviceSelector = null;

            deviceConnectedCallback = null;
            deviceCloseCallback = null;
            appSuspendCallback = null;

            isEnabledAutoReconnect = true;
        }

        /// <summary>
        /// If this event handler will be running in a background task, app events will not be registered for because they are of
        /// no use to the background task.
        /// </summary>
        /// <param name="isBackgroundTask">Whether or not the event handler will be running as a background task</param>
        private EventHandlerForSerialDevice(bool isBackgroundTask)
        {
            watcherStarted = false;
            watcherSuspended = false;
            isEnabledAutoReconnect = true;
            this.isBackgroundTask = isBackgroundTask;
        }

        /// <summary>
        /// Closes the device, stops the device watcher, stops listening for app events, and resets object state to before a device
        /// was ever connected.
        /// 
        /// When the SerialDevice is closing, it will cancel all IO operations that are still pending (not complete).
        /// The close will not wait for any IO completion callbacks to be called, so the close call may complete before any of
        /// the IO completion callbacks are called.
        /// The pending IO operations will still call their respective completion callbacks with either a task 
        /// canceled error or the operation completed.
        /// </summary>
        private void CloseCurrentlyConnectedDevice()
        {
            if (device != null)
            {
                // Notify callback that we're about to close the device
                deviceCloseCallback?.Invoke(this, deviceInformation);

                //Debug.WriteLine($"Closing device {deviceInformation.Id}");

                // This closes the handle to the device
                device?.Dispose();

                device = null;
            }
        }

        /// <summary>
        /// Register for Added and Removed events.
        /// Note that, when disconnecting the device, the device may be closed by the system before the OnDeviceRemoved callback is invoked.
        /// </summary>
        private void RegisterForDeviceWatcherEvents()
        {
            deviceAddedEventHandler = new TypedEventHandler<DeviceWatcher, DeviceInformation>(OnDeviceAdded);

            deviceRemovedEventHandler = new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(OnDeviceRemoved);

            deviceWatcher.Added += deviceAddedEventHandler;

            deviceWatcher.Removed += deviceRemovedEventHandler;
        }

        private void UnregisterFromDeviceWatcherEvents()
        {
            deviceWatcher.Added -= deviceAddedEventHandler;
            deviceAddedEventHandler = null;

            deviceWatcher.Removed -= deviceRemovedEventHandler;
            deviceRemovedEventHandler = null;
        }

        private void UnregisterFromDeviceAccessStatusChange()
        {
            deviceAccessInformation.AccessChanged -= deviceAccessEventHandler;

            deviceAccessEventHandler = null;
        }

        private void StartDeviceWatcher()
        {
            watcherStarted = true;

            if ((deviceWatcher.Status != DeviceWatcherStatus.Started)
                && (deviceWatcher.Status != DeviceWatcherStatus.EnumerationCompleted))
            {
                deviceWatcher.Start();
            }
        }

        private void StopDeviceWatcher()
        {
            if ((deviceWatcher.Status == DeviceWatcherStatus.Started)
                || (deviceWatcher.Status == DeviceWatcherStatus.EnumerationCompleted))
            {
                deviceWatcher.Stop();
            }

            watcherStarted = false;
        }

        /// <summary>
        /// When resume into the application, we should reopen a handle to the Serial device again. This will automatically
        /// happen when we start the device watcher again; the device will be re-enumerated and we will attempt to reopen it
        /// if IsEnabledAutoReconnect property is enabled.
        /// 
        /// See OnAppSuspension for why we are starting the device watcher again
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="arg"></param>
        private void OnAppResume(object sender, object args)
        {
            if (watcherSuspended)
            {
                watcherSuspended = false;
                StartDeviceWatcher();
            }
        }

        /// <summary>
        /// Close the device that is opened so that all pending operations are canceled properly.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="deviceInformationUpdate"></param>
        private void OnDeviceRemoved(DeviceWatcher sender, DeviceInformationUpdate deviceInformationUpdate)
        {
            if (IsDeviceConnected && (deviceInformationUpdate.Id == deviceInformation.Id))
            {
                // The main reasons to close the device explicitly is to clean up resources, to properly handle errors,
                // and stop talking to the disconnected device.
                CloseCurrentlyConnectedDevice();
            }
        }

        /// <summary>
        /// Open the device that the user wanted to open if it hasn't been opened yet and auto reconnect is enabled.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="deviceInfo"></param>
        private void OnDeviceAdded(DeviceWatcher sender, DeviceInformation deviceInfo)
        {
            if ((deviceInformation != null) && (deviceInfo.Id == deviceInformation.Id) && !IsDeviceConnected && isEnabledAutoReconnect)
            {
            }
        }

        /// <summary>
        /// Close the device if the device access was denied by anyone (system or the user) and reopen it if permissions are allowed again
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void OnDeviceAccessChanged(DeviceAccessInformation sender, DeviceAccessChangedEventArgs eventArgs)
        {
            if ((eventArgs.Status == DeviceAccessStatus.DeniedBySystem)
                || (eventArgs.Status == DeviceAccessStatus.DeniedByUser))
            {
                CloseCurrentlyConnectedDevice();
            }
            else if ((eventArgs.Status == DeviceAccessStatus.Allowed) && (deviceInformation != null) && isEnabledAutoReconnect)
            {
            }
        }
    }
}
