//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;


namespace nanoFramework.Tools.Debugger.Serial
{
    public class SerialDeviceInformation
    {
        private readonly string deviceSelector;

        public string InstanceId
        {
            get
            {
                return "";
            }
        }

        /// <summary>
        /// The class is mainly used as a DeviceInformation wrapper so that the UI can bind to a list of these.
        /// </summary>
        /// <param name="deviceInformation"></param>
        /// <param name="deviceSelector">The AQS used to find this device</param>
        public SerialDeviceInformation(String deviceSelector)
        {
            this.deviceSelector = deviceSelector;
        }
    }
}
