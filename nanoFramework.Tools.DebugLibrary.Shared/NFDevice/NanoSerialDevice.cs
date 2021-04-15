//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.Serial;

namespace nanoFramework.Tools.Debugger
{
    public class NanoSerialDevice
    {
        /// <summary>
        /// Default timeout for serial device (in milliseconds).
        /// </summary>
        public const int SafeDefaultTimeout = 1000;

        public SerialDeviceInformation DeviceInformation { get; set; }
    }
}
