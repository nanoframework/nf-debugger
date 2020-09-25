//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Foundation;

namespace nanoFramework.Tools.Debugger.Usb
{
    /// <summary>
    /// This class handles the required changes and operation of an UsbDevice when a specific app event
    /// is raised (app suspension and resume) or when the device is disconnected. The device watcher events are also handled here.
    /// </summary>
    public partial class EventHandlerForUsbDevice
    {
        /// <summary>
        /// Listen for any changed in device access permission. The user can block access to the device while the device is in use.
        /// If the user blocks access to the device while the device is opened, the device's handle will be closed automatically by
        /// the system; it is still a good idea to close the device explicitly so that resources are cleaned up.
        /// 
        /// Note that by the time the AccessChanged event is raised, the device handle may already be closed by the system.
        /// </summary>
        private void RegisterForDeviceAccessStatusChange()
        {
            //deviceAccessInformation = DeviceAccessInformation.CreateFromId(deviceInformation.Id);

            //deviceAccessEventHandler = new TypedEventHandler<DeviceAccessInformation, DeviceAccessChangedEventArgs>(OnDeviceAccessChanged);
            //deviceAccessInformation.AccessChanged += deviceAccessEventHandler;
        }

        /// <summary>
        /// This method opens the device using the WinRT Usb API. After the device is opened, save the device
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
        public async Task<bool> OpenDeviceAsync(DeviceInformation deviceInfo, String deviceSelector)
        {
#pragma warning disable ConfigureAwaitChecker // CAC001
            device = await Windows.Devices.Usb.UsbDevice.FromIdAsync(deviceInfo.Id);
#pragma warning restore ConfigureAwaitChecker // CAC001

            bool successfullyOpenedDevice = false;

            try
            {
                // Device could have been blocked by user or the device has already been opened by another app.
                if (device != null)
                {
                    successfullyOpenedDevice = true;

                    deviceInformation = deviceInfo;
                    this.deviceSelector = deviceSelector;

                    Debug.WriteLine($"Device {deviceInformation.Id} opened");

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

                    // Most likely the device is opened by another app, but cannot be sure
                    Debug.WriteLine($"Unknown error, possibly opened by another app : {deviceInfo.Id}");
                }
            }
            // catch all because the device open might fail for a number of reasons
            catch { }

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
            else if ((eventArgs.Status == DeviceAccessStatus.Allowed) && (deviceInformation != null) && isEnabledAutoReconnect)
            {
            }
        }
    }
}
