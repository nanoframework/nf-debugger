//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.PortSerial;
using nanoFramework.Tools.Debugger.Usb;
using System;
using System.Collections.ObjectModel;
using Windows.UI.Xaml;

namespace nanoFramework.Tools.Debugger
{
    public abstract partial class PortBase
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
