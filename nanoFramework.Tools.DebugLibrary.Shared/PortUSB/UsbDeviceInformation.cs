//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using Windows.Devices.Enumeration;

namespace nanoFramework.Tools.Debugger.Usb
{
    /// <summary>
    /// The class will only expose properties from DeviceInformation that are going to be used
    /// in this sample. Each instance of this class provides information about a single device.
    ///
    /// This class is used by the UI to display device specific information so that
    /// the user can identify which device to use.
    /// </summary>
    public class UsbDeviceInformation
    {
        private readonly DeviceInformation device;
        private readonly string deviceSelector;

        public string InstanceId
        {
            get
            {
                return (string)device.Properties[UsbDeviceProperties.DeviceInstanceId];
            }
        }

        public DeviceInformation DeviceInformation
        {
            get
            {
                return device;
            }
        }

        public string DeviceSelector
        {
            get
            {
                return deviceSelector;
            }
        }

        /// <summary>
        /// The class is mainly used as a DeviceInformation wrapper so that the UI can bind to a list of these.
        /// </summary>
        /// <param name="deviceInformation"></param>
        /// <param name="deviceSelector">The AQS used to find this device</param>
        public UsbDeviceInformation(DeviceInformation deviceInformation, String deviceSelector)
        {
            device = deviceInformation;
            this.deviceSelector = deviceSelector;
        }
    }
}
