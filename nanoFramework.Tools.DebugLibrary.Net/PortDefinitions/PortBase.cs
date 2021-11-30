//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.PortSerial;
using System.Collections.Generic;
using nanoFramework.Tools.Debugger.PortComposite;
using nanoFramework.Tools.Debugger.PortTcpIp;

namespace nanoFramework.Tools.Debugger
{
    public abstract partial class PortBase : PortMessageBase
    {
        public static PortBase CreateInstanceForSerial()
        {
            return new PortSerialManager(true, null);
        }

        public static PortBase CreateInstanceForSerial(List<string> portExclusionList)
        {
            return new PortSerialManager(true, portExclusionList);
        }

        public static PortBase CreateInstanceForSerial(bool startDeviceWatchers, int bootTime = 1000)
        {
            return new PortSerialManager(startDeviceWatchers, null, bootTime);
        }

        public static PortBase CreateInstanceForSerial(bool startDeviceWatchers, List<string> portExclusionList = null, int bootTime = 1000)
        {
            return new PortSerialManager(startDeviceWatchers, portExclusionList, bootTime);
        }
        
        public static PortBase CreateInstanceForNetwork(bool startDeviceWatchers)
        {
            return new PortTcpIpDeviceManager(startDeviceWatchers);
        }
        
        public static PortBase CreateInstanceForNetwork(bool startDeviceWatchers, int discoveryPort)
        {
            return new PortTcpIpDeviceManager(startDeviceWatchers, discoveryPort);
        }
        
        public static PortBase CreateInstanceForComposite(IEnumerable<PortBase> ports, bool startDeviceWatchers)
        {
            return new PortCompositeDeviceManager(ports, startDeviceWatchers);
        }
    }
}
