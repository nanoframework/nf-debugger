//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.PortSerial;

namespace nanoFramework.Tools.Debugger
{
    public abstract partial class PortBase : PortMessageBase
    {
        public static PortBase CreateInstanceForSerial(string displayName, object callerApp = null, bool startDeviceWatchers = true, int bootTime = 1500)
        {
            return new SerialPortManager(callerApp, startDeviceWatchers, bootTime);
        }
    }
}
