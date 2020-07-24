//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.PortSerial;
using System.Collections.Generic;

namespace nanoFramework.Tools.Debugger
{
    public abstract partial class PortBase : PortMessageBase
    {
        public static PortBase CreateInstanceForSerial(string displayName, List<string> comPortBlackList = null)
        {
            return new SerialPortManager(null, true, comPortBlackList, 1000);
        }

        public static PortBase CreateInstanceForSerial(string displayName, object callerApp = null, bool startDeviceWatchers = true, int bootTime = 1000)
        {
            return new SerialPortManager(callerApp, startDeviceWatchers, null, bootTime);
        }

        public static PortBase CreateInstanceForSerial(string displayName, object callerApp = null, bool startDeviceWatchers = true, List<string> comPortBlackList = null, int bootTime = 1000)
        {
            return new SerialPortManager(callerApp, startDeviceWatchers, comPortBlackList, bootTime);
        }
    }
}
