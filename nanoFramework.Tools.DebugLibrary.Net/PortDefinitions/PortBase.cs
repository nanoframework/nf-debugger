//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.PortSerial;
using System.Collections.Generic;

namespace nanoFramework.Tools.Debugger
{
    public abstract partial class PortBase : PortMessageBase
    {       
        public static PortBase CreateInstanceForSerial(string displayName, List<string> portBlackList = null)
        {
            return new SerialPortManager(null, true, portBlackList);
        }

        public static PortBase CreateInstanceForSerial(string displayName, bool startDeviceWatchers = true, List<string> portBlackList = null)
        {
            return new SerialPortManager(null, startDeviceWatchers, portBlackList);
        }

        public static PortBase CreateInstanceForSerial(string displayName, object callerApp = null, bool startDeviceWatchers = true, int bootTime = 1000)
        {
            return new SerialPortManager(callerApp, startDeviceWatchers, null, bootTime);
        }

        public static PortBase CreateInstanceForSerial(string displayName, object callerApp = null, bool startDeviceWatchers = true, List<string> portBlackList = null, int bootTime = 1000)
        {
            return new SerialPortManager(callerApp, startDeviceWatchers, portBlackList, bootTime);
        }
    }
}
