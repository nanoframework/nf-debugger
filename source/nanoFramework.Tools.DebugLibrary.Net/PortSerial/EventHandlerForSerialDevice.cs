//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;

namespace nanoFramework.Tools.Debugger.Serial
{
    /// <summary>
    /// This class handles the required changes and operation of an SerialDevice when a specific app event
    /// is raised (app suspension and resume) or when the device is disconnected. The device watcher events are also handled here.
    /// </summary>
    public partial class EventHandlerForSerialDevice
    {
        private EventHandler _appSuspendEventHandler;
        private EventHandler _appResumeEventHandler;

        /// <summary>
        /// Register for app suspension/resume events. See the comments
        /// for the event handlers for more information on what is being done to the device.
        ///
        /// We will also register for when the app exists so that we may close the device handle.
        /// </summary>
        private void RegisterForAppEvents()
        {
            _appSuspendEventHandler = new EventHandler(Current.OnAppDeactivated);
            _appResumeEventHandler = new EventHandler(Current.OnAppResume);

            // This event is raised when the app is exited and when the app is suspended
            CallerApp.Deactivated += _appSuspendEventHandler;

            CallerApp.Activated += _appResumeEventHandler;
        }

        private void UnregisterFromAppEvents()
        {
            // This event is raised when the app is exited and when the app is suspended
            CallerApp.Deactivated -= _appSuspendEventHandler;

            CallerApp.Activated -= _appResumeEventHandler;
        }

        /// <summary>
        /// This is an empty method just to keep the workflow unchanged between UWP and Desktop version.
        /// The DeviceAccessStatusChange is only available in UWP.
        /// </summary>
        private void RegisterForDeviceAccessStatusChange()
        {

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
        private void OnAppDeactivated(object sender, EventArgs args)
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

            //// Forward suspend event to registered callback function
            //if (appSuspendCallback != null)
            //{
            //    appSuspendCallback(sender, args);
            //}

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
        public async Task<bool> OpenDeviceAsync(DeviceInformation deviceInfo, string deviceSelector)
        {
            bool successfullyOpenedDevice = false;

            _device = await SerialDevice.FromIdAsync(deviceInfo.Id);

            try
            {
                // Device could have been blocked by user or the device has already been opened by another app.
                if (_device != null)
                {
                    successfullyOpenedDevice = true;

                    _deviceInformation = deviceInfo;
                    _deviceSelector = deviceSelector;

                    // adjust settings for serial port
                    _device.BaudRate = 115200;
                    _device.DataBits = 8;

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
                }
            }
            // catch all because the device open might fail for a number of reasons
            catch (Exception ex)
            {
            }

            return successfullyOpenedDevice;
        }
    }
}
