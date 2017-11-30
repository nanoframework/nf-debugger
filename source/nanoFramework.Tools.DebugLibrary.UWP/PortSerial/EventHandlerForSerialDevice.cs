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
using Windows.UI.Xaml;

namespace nanoFramework.Tools.Debugger.Serial
{
    /// <summary>
    /// This class handles the required changes and operation of an SerialDevice when a specific app event
    /// is raised (app suspension and resume) or when the device is disconnected. The device watcher events are also handled here.
    /// </summary>
    public partial class EventHandlerForSerialDevice
    {
        private SuspendingEventHandler _appSuspendEventHandler;
        private EventHandler<object> _appResumeEventHandler;

        private SuspendingEventHandler _appSuspendCallback;

        public SuspendingEventHandler OnAppSuspendCallback
        {
            get
            {
                return _appSuspendCallback;
            }

            set
            {
                _appSuspendCallback = value;
            }
        }

        /// <summary>
        /// Register for app suspension/resume events. See the comments
        /// for the event handlers for more information on what is being done to the device.
        ///
        /// We will also register for when the app exists so that we may close the device handle.
        /// </summary>
        private void RegisterForAppEvents()
        {
            _appSuspendEventHandler = new SuspendingEventHandler(Current.OnAppSuspension);
            _appResumeEventHandler = new EventHandler<object>(Current.OnAppResume);

            // This event is raised when the app is exited and when the app is suspended
            CallerApp.Suspending += _appSuspendEventHandler;

            CallerApp.Resuming += _appResumeEventHandler;
        }

        private void UnregisterFromAppEvents()
        {
            // This event is raised when the app is exited and when the app is suspended
            CallerApp.Suspending -= _appSuspendEventHandler;

            CallerApp.Resuming -= _appResumeEventHandler;
        }

        /// <summary>
        /// Listen for any changed in device access permission. The user can block access to the device while the device is in use.
        /// If the user blocks access to the device while the device is opened, the device's handle will be closed automatically by
        /// the system; it is still a good idea to close the device explicitly so that resources are cleaned up.
        /// 
        /// Note that by the time the AccessChanged event is raised, the device handle may already be closed by the system.
        /// </summary>
        private void RegisterForDeviceAccessStatusChange()
        {
            _deviceAccessInformation = DeviceAccessInformation.CreateFromId(_deviceInformation.Id);

            _deviceAccessEventHandler = new TypedEventHandler<DeviceAccessInformation, DeviceAccessChangedEventArgs>(OnDeviceAccessChanged);
            _deviceAccessInformation.AccessChanged += _deviceAccessEventHandler;
        }

        /// <summary>
        /// If a SerialDevice object has been instantiated (a handle to the device is opened), we must close it before the app 
        /// goes into suspension because the API automatically closes it for us if we don't. When resuming, the API will
        /// not reopen the device automatically, so we need to explicitly open the device in that situation.
        ///
        /// Since we have to reopen the device ourselves when the app resumes, it is good practice to explicitly call the close
        /// in the app as well (For every open there is a close).
        /// 
        /// We must stop the DeviceWatcher because it will continue to raise events even if
        /// the app is in suspension, which is not desired (drains battery). We resume the device watcher once the app resumes again.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void OnAppSuspension(object sender, Windows.ApplicationModel.SuspendingEventArgs args)
        {
            if (_watcherStarted)
            {
                _watcherSuspended = true;
                StopDeviceWatcher();
            }
            else
            {
                _watcherSuspended = false;
            }

            // Forward suspend event to registered callback function
            if (_appSuspendCallback != null)
            {
                _appSuspendCallback(sender, args);
            }

            CloseCurrentlyConnectedDevice();
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
        public async Task<bool> OpenDeviceAsync(DeviceInformation deviceInfo, string deviceSelector, SerialDevice existingDevice)
        {
            await Task.Delay(250);

            bool successfullyOpenedDevice = false;

            if (existingDevice == null)
            {
                _device = await SerialDevice.FromIdAsync(deviceInfo.Id);
            }
            else
            {
                _device = existingDevice;
            }

            try
            {
                // Device could have been blocked by user or the device has already been opened by another app.
                if (_device != null)
                {
                    successfullyOpenedDevice = true;

                    _deviceInformation = deviceInfo;
                    this._deviceSelector = deviceSelector;

                    Debug.WriteLine($"Device {_deviceInformation.Id} opened");

                    // adjust settings for serial port
                    _device.BaudRate = 115200;

                    /////////////////////////////////////////////////////////////
                    // need to FORCE the parity setting to _NONE_ because        
                    // the default on the current ST Link is different causing 
                    // the communication to fail
                    /////////////////////////////////////////////////////////////
                    _device.Parity = SerialParity.None;

                    _device.WriteTimeout = TimeSpan.FromMilliseconds(1000);
                    _device.ReadTimeout = TimeSpan.FromMilliseconds(1000);
                    _device.ErrorReceived += Device_ErrorReceived;

                    // Notify registered callback handle that the device has been opened
                    _deviceConnectedCallback?.Invoke(this, _deviceInformation);

                    // Background tasks are not part of the app, so app events will not have an affect on the device
                    if (!_isBackgroundTask && (_appSuspendEventHandler == null || _appResumeEventHandler == null))
                    {
                        RegisterForAppEvents();
                    }

                    // User can block the device after it has been opened in the Settings charm. We can detect this by registering for the 
                    // DeviceAccessInformation.AccessChanged event
                    if (_deviceAccessEventHandler == null)
                    {
                        RegisterForDeviceAccessStatusChange();
                    }

                    // Create and register device watcher events for the device to be opened unless we're reopening the device
                    if (_deviceWatcher == null)
                    {
                        _deviceWatcher = DeviceInformation.CreateWatcher(deviceSelector);

                        RegisterForDeviceWatcherEvents();
                    }

                    if (!_watcherStarted)
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
                            Debug.WriteLine($"Access to the device was blocked by the user : {deviceInfo.Id}");
                            break;

                        case DeviceAccessStatus.DeniedBySystem:
                            // This status is most likely caused by app permissions (did not declare the device in the app's package.appxmanifest)
                            // This status does not cover the case where the device is already opened by another app.
                            Debug.WriteLine($"Access to the device was blocked by the system : {deviceInfo.Id}");
                            break;

                        default:
                            // Most likely the device is opened by another app, but cannot be sure
                            Debug.WriteLine($"Unknown error, possibly opened by another app : {deviceInfo.Id}");
                            break;
                    }
                }
            }
            // catch all because the device open might fail for a number of reasons
            catch (Exception ex)
            {
            }

            return successfullyOpenedDevice;
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
            else if ((eventArgs.Status == DeviceAccessStatus.Allowed) && (_deviceInformation != null) && _isEnabledAutoReconnect)
            {
            }
        }
    }
}
