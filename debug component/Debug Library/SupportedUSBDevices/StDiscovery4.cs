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

namespace Microsoft.SPOT.Debugger.Usb
{
    public class StDiscovery4
    {
        public const UInt16 DeviceVid = 0x0483;

        public const UInt16 DevicePid = 0xA08F;

        /// <summary>
        /// USB device interface class GUID. For NETMF debug capable devices this must be {D32D1D64-963D-463E-874A-8EC8C8082CBF}.
        /// </summary>
        public static Guid DeviceInterfaceClass = new Guid("{D32D1D64-963D-463E-874A-8EC8C8082CBF}");

        /// <summary>
        /// ID string for the device
        /// </summary>
        public static String IDString
        {
            get { return String.Format("VID_{0:X4}&PID_{1:X4}", DeviceVid, DevicePid); }
        }

        public static new string ToString()
        {
            return "ST Discovery4";
        }
    }
}
