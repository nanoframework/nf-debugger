//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.PortSerial;
using nanoFramework.Tools.Debugger.Usb;
using Windows.UI.Xaml;

namespace nanoFramework.Tools.Debugger
{
    public abstract partial class PortBase : PortMessageBase
    {
        public static PortBase CreateInstanceForSerial(string displayName, Application callerApp, bool startDeviceWatchers = true)
        {
            return new SerialPortManager(callerApp, startDeviceWatchers);
        }

        public static PortBase CreateInstanceForUsb(string displayName, Application callerApp, bool startDeviceWatchers = true)
        {
            return new UsbPort(callerApp, startDeviceWatchers);
        }
    }
}
