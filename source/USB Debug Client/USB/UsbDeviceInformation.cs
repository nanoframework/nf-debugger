//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the Microsoft .NET Micro Framework and is unsupported. 
// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use these files except in compliance with the License.
// You may obtain a copy of the License at:
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing
// permissions and limitations under the License.
// 
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using Windows.Devices.Enumeration;

namespace Microsoft.NetMicroFramework.Tools.UsbDebug
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
        private DeviceInformation device;
        private string deviceSelector;

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
        public UsbDeviceInformation(Windows.Devices.Enumeration.DeviceInformation deviceInformation, String deviceSelector)
        {
            device = deviceInformation;
            this.deviceSelector = deviceSelector;
        }
    }
}
