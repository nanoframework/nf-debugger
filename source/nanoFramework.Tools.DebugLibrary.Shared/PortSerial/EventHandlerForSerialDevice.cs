//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
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
        private static volatile EventHandlerForSerialDevice s_eventHandlerForNanoFrameworkDevice;

        /// <summary>
        /// Used to synchronize threads to avoid multiple instantiations of eventHandlerForSerialEclo.
        /// </summary>
        private static object s_singletonCreationLock = new object();

        private string _deviceSelector;
        private DeviceWatcher _deviceWatcher;

        private DeviceInformation _deviceInformation;
        private DeviceAccessInformation _deviceAccessInformation;
        private SerialDevice _device;

        private TypedEventHandler<EventHandlerForSerialDevice, DeviceInformation> _deviceCloseCallback;
        private TypedEventHandler<EventHandlerForSerialDevice, DeviceInformation> _deviceConnectedCallback;

        private TypedEventHandler<DeviceWatcher, DeviceInformation> _deviceAddedEventHandler;
        private TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> _deviceRemovedEventHandler;
        private TypedEventHandler<DeviceAccessInformation, DeviceAccessChangedEventArgs> _deviceAccessEventHandler;

        private bool _watcherSuspended;
        private bool _watcherStarted;

        private bool _isBackgroundTask;
        private bool _isEnabledAutoReconnect;

        // A pointer back to the calling app.  This is needed to reach methods and events there 
#if WINDOWS_UWP
        public static Windows.UI.Xaml.Application CallerApp { get; set; }
#else
        public static System.Windows.Application CallerApp { get; set; }
#endif

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
                lock (s_singletonCreationLock)
                {
                    if (s_eventHandlerForNanoFrameworkDevice == null)
                    {
                        if (CallerApp != null)
                        {
                            // there is a CallerApp set so assume this is NOT a background task
                            CreateNewEventHandlerForDevice();
                        }
                        else
                        {
                            // there is NO CallerApp set so assume this IS a background task
                            CreateNewEventHandlerForDeviceForBackgroundTasks();
                        }
                    }
                }

                return s_eventHandlerForNanoFrameworkDevice;
            }
        }

        /// <summary>
        /// Creates a new instance of EventHandlerForSerialEclo, enables auto reconnect, and uses it as the Current instance.
        /// </summary>
        public static void CreateNewEventHandlerForDevice()
        {
            s_eventHandlerForNanoFrameworkDevice = new EventHandlerForSerialDevice(false);
        }

        /// <summary>
        /// Creates a new instance of EventHandlerForSerialEclo, disables auto reconnect, and uses it as the Current instance.
        /// Background tasks do not need to worry about app events, so we will not be registering for app events
        /// </summary>
        public static void CreateNewEventHandlerForDeviceForBackgroundTasks()
        {
            s_eventHandlerForNanoFrameworkDevice = new EventHandlerForSerialDevice(true);
        }


        public TypedEventHandler<EventHandlerForSerialDevice, DeviceInformation> OnDeviceClose
        {
            get
            {
                return _deviceCloseCallback;
            }

            set
            {
                _deviceCloseCallback = value;
            }
        }

        public TypedEventHandler<EventHandlerForSerialDevice, DeviceInformation> OnDeviceConnected
        {
            get
            {
                return _deviceConnectedCallback;
            }

            set
            {
                _deviceConnectedCallback = value;
            }
        }

        public bool IsDeviceConnected
        {
            get
            {
                return (_device != null);
            }
        }

        public SerialDevice Device
        {
            get
            {
                return _device;
            }

            internal set
            {
                _device = value;
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
                return _deviceInformation;
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
                return _deviceAccessInformation;
            }
        }

        /// <summary>
        /// DeviceSelector AQS used to find this device
        /// </summary>
        public String DeviceSelector
        {
            get
            {
                return _deviceSelector;
            }
        }

        /// <summary>
        /// True if EventHandlerForSerialEclo will attempt to reconnect to the device once it is plugged into the computer again
        /// </summary>
        public bool IsEnabledAutoReconnect
        {
            get
            {
                return _isEnabledAutoReconnect;
            }
            set
            {
                _isEnabledAutoReconnect = value;
            }
        }

        private void Device_ErrorReceived(SerialDevice sender, ErrorReceivedEventArgs args)
        {
            throw new NotImplementedException();
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

            _deviceInformation = null;
            _deviceSelector = null;

            _isEnabledAutoReconnect = true;

            Debug.WriteLine($"##################");
            Current._device?.Dispose();
            Current._device = null;
        }

        /// <summary>
        /// If this event handler will be running in a background task, app events will not be registered for because they are of
        /// no use to the background task.
        /// </summary>
        /// <param name="isBackgroundTask">Whether or not the event handler will be running as a background task</param>
        private EventHandlerForSerialDevice(bool isBackgroundTask)
        {
            _watcherStarted = false;
            _watcherSuspended = false;
            _isEnabledAutoReconnect = true;
            this._isBackgroundTask = isBackgroundTask;
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
            if (_device != null)
            {
                // Notify callback that we're about to close the device
                _deviceCloseCallback?.Invoke(this, _deviceInformation);

                Debug.WriteLine($"Closing device {_deviceInformation?.Id}");

                // This closes the handle to the device
                _device.Dispose();
            }
        }

        /// <summary>
        /// Register for Added and Removed events.
        /// Note that, when disconnecting the device, the device may be closed by the system before the OnDeviceRemoved callback is invoked.
        /// </summary>
        private void RegisterForDeviceWatcherEvents()
        {
            _deviceAddedEventHandler = new TypedEventHandler<DeviceWatcher, DeviceInformation>(OnDeviceAdded);

            _deviceRemovedEventHandler = new TypedEventHandler<DeviceWatcher, DeviceInformationUpdate>(OnDeviceRemoved);

            _deviceWatcher.Added += _deviceAddedEventHandler;

            _deviceWatcher.Removed += _deviceRemovedEventHandler;
        }

        private void UnregisterFromDeviceWatcherEvents()
        {
            _deviceWatcher.Added -= _deviceAddedEventHandler;
            _deviceAddedEventHandler = null;

            _deviceWatcher.Removed -= _deviceRemovedEventHandler;
            _deviceRemovedEventHandler = null;
        }

        private void UnregisterFromDeviceAccessStatusChange()
        {
            _deviceAccessInformation.AccessChanged -= _deviceAccessEventHandler;

            _deviceAccessEventHandler = null;
        }

        private void StartDeviceWatcher()
        {
            _watcherStarted = true;

            if ((_deviceWatcher.Status != DeviceWatcherStatus.Started)
                && (_deviceWatcher.Status != DeviceWatcherStatus.EnumerationCompleted))
            {
                _deviceWatcher.Start();
            }
        }

        private void StopDeviceWatcher()
        {
            if ((_deviceWatcher.Status == DeviceWatcherStatus.Started)
                || (_deviceWatcher.Status == DeviceWatcherStatus.EnumerationCompleted))
            {
                _deviceWatcher.Stop();
            }

            _watcherStarted = false;
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
            if (_watcherSuspended)
            {
                _watcherSuspended = false;
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
            if (IsDeviceConnected && (deviceInformationUpdate.Id == _deviceInformation.Id))
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
            if ((_deviceInformation != null) && (deviceInfo.Id == _deviceInformation.Id) && !IsDeviceConnected && _isEnabledAutoReconnect)
            {
            }
        }
    }
}
