//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger
{
    using System;
    using System.Diagnostics.Tracing;

    [EventSource(Name = "nanoFramework-NanoDevices")]
    internal class NanoDevicesEventSource : EventSource
    {
        private const string GUID_DEVINTERFACE_COMPORT = "{86e0d1e0-8089-11d0-9ce4-08003e301f73}";
        private const string DEVICE_INSTANCE = "#0000#";

        public static NanoDevicesEventSource Log { get { return Log_.Value; } }
        private static readonly Lazy<NanoDevicesEventSource> Log_ = new Lazy<NanoDevicesEventSource>(() => new NanoDevicesEventSource());

        [Event(1, Level = EventLevel.Informational, Opcode = EventOpcode.Info)]
        public string DroppingBlackListedDevice(string deviceSelector)
        {
            string logMessage = $"NanoDevices: dropping black listed device {deviceSelector}";

            WriteEvent(1, logMessage);

            return logMessage;
        }

        [Event(2, Level = EventLevel.Informational, Opcode = EventOpcode.Info)]
        public string DeviceArrival(string deviceId)
        {
            string logMessage = $"NanoDevices: new device arrival {deviceId.Replace(GUID_DEVINTERFACE_COMPORT, "").Replace(DEVICE_INSTANCE, "")} ";

            WriteEvent(2, logMessage);

            return logMessage;
        }

        [Event(3, Level = EventLevel.Informational, Opcode = EventOpcode.Info)]
        public string CandidateDevice(string deviceId)
        {
            string logMessage = $"NanoDevices: candidate nano device {deviceId.Replace(GUID_DEVINTERFACE_COMPORT, "").Replace(DEVICE_INSTANCE, "")}";

            WriteEvent(3, logMessage);

            return logMessage;
        }

        [Event(4, Level = EventLevel.Informational, Opcode = EventOpcode.Info)]
        public string ValidDevice(string deviceId)
        {
            string logMessage = $"NanoDevices: valid device {deviceId.Replace(GUID_DEVINTERFACE_COMPORT, "").Replace(DEVICE_INSTANCE, "")}";

            WriteEvent(4, logMessage);

            return logMessage;
        }

        [Event(5, Level = EventLevel.Informational, Opcode = EventOpcode.Info)]
        public string SerialDeviceEnumerationCompleted(int deviceCount)
        {
            string logMessage = $"NanoDevices: Serial device enumeration completed. Found {deviceCount} devices";

            WriteEvent(5, logMessage);

            return logMessage;
        }

        [Event(6, Level = EventLevel.Informational, Opcode = EventOpcode.Info)]
        public string CheckingValidDevice(string deviceId)
        {
            string logMessage = $"NanoDevices: checking device {deviceId.Replace(GUID_DEVINTERFACE_COMPORT, "").Replace(DEVICE_INSTANCE, "")}";

            WriteEvent(6, logMessage);

            return logMessage;
        }

        [Event(7, Level = EventLevel.Critical, Opcode = EventOpcode.Info)]
        public string CriticalError(string errorMessage)
        {
            string logMessage = $"NanoDevices: {errorMessage}";

            WriteEvent(7, logMessage);

            return logMessage;
        }

        [Event(8, Level = EventLevel.Informational, Opcode = EventOpcode.Info)]
        public string QuitDevice(string deviceId)
        {
            string logMessage = $"NanoDevices: quitting device {deviceId.Replace(GUID_DEVINTERFACE_COMPORT, "").Replace(DEVICE_INSTANCE, "")}";

            WriteEvent(8, logMessage);

            return logMessage;
        }

        [Event(8, Level = EventLevel.Informational, Opcode = EventOpcode.Info)]
        public string OpenDevice(string deviceId)
        {
            string logMessage = $"NanoDevices: open device {deviceId.Replace(GUID_DEVINTERFACE_COMPORT, "").Replace(DEVICE_INSTANCE, "")}";

            WriteEvent(8, logMessage);

            return logMessage;
        }

        [Event(9, Level = EventLevel.Informational, Opcode = EventOpcode.Info)]
        public string CloseDevice(string deviceId)
        {
            string logMessage = $"NanoDevices: close device {deviceId.Replace(GUID_DEVINTERFACE_COMPORT, "").Replace(DEVICE_INSTANCE, "")}";

            WriteEvent(9, logMessage);

            return logMessage;
        }

        [Event(10, Level = EventLevel.Informational, Opcode = EventOpcode.Info)]
        public string UsbDeviceEnumerationCompleted(int deviceCount)
        {
            string logMessage = $"NanoDevices: USB device enumeration completed. Found {deviceCount} devices";

            WriteEvent(10, logMessage);

            return logMessage;
        }

        [Event(11, Level = EventLevel.Informational, Opcode = EventOpcode.Info)]
        public string DeviceDeparture(string deviceId)
        {
            string logMessage = $"NanoDevices: device departure {deviceId.Replace(GUID_DEVINTERFACE_COMPORT, "").Replace(DEVICE_INSTANCE, "")} ";

            WriteEvent(11, logMessage);

            return logMessage;
        }

        private NanoDevicesEventSource()
        {
        }
    }
}
