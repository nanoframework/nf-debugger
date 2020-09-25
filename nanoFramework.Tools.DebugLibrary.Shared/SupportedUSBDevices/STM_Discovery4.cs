//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger.Usb
{
    // This class is kept here for reference only.
    // It was to provide backwards compatibility with NETMF WinUSB devices of v4.4
    // In the current nanoFramework implementation USB connection with devices is carried using USB CDC

    public class STM_Discovery4
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
