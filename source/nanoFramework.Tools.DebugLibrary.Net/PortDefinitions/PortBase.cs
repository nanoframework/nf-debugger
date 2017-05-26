//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.PortSerial;
using nanoFramework.Tools.Debugger.Usb;
using System;
using System.Collections.ObjectModel;
using System.Windows;

namespace nanoFramework.Tools.Debugger
{
    public abstract partial class PortBase
    {
        public static PortBase CreateInstanceForSerial(string displayName, Application callerApp)
        {
            return new SerialPort(callerApp);
        }

        //public static PortBase CreateInstanceForUsb(string displayName, Application callerApp)
        //{
        //    return new UsbPort(callerApp);
        //}
    }
}
