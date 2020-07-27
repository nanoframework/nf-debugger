//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using Windows.Devices.Enumeration;

namespace nanoFramework.Tools.Debugger.Serial
{
    public class SerialDeviceInformation
    {
        private readonly DeviceInformation device;
        private readonly string deviceSelector;

        public string InstanceId
        {
            get
            {
                return (string)device.Properties[SerialDeviceProperties.DeviceInstanceId];
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
        public SerialDeviceInformation(DeviceInformation deviceInformation, String deviceSelector)
        {
            device = deviceInformation;
            this.deviceSelector = deviceSelector;
        }
    }
}
