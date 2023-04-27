//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.PortComposite;
using nanoFramework.Tools.Debugger.PortSerial;
using nanoFramework.Tools.Debugger.PortTcpIp;
using System.Collections.Generic;

namespace nanoFramework.Tools.Debugger
{
    //  write intellisense documentation for this class  
    public abstract partial class PortBase : PortMessageBase
    {

        #region creating serial instances

        public static PortBase CreateInstanceForSerial()
        {
            return new PortSerialManager(true, null);
        }

        public static PortBase CreateInstanceForSerial(List<string> portExclusionList)
        {
            return new PortSerialManager(
                true,
                portExclusionList);
        }
        public static PortBase CreateInstanceForSerial(bool startDeviceWatchers)
        {
            return new PortSerialManager(
                startDeviceWatchers,
                null);
        }

        public static PortBase CreateInstanceForSerial(
            bool startDeviceWatchers,
            int bootTime = 1000)
        {
            return new PortSerialManager(
                startDeviceWatchers,
                null,
                bootTime);
        }

        public static PortBase CreateInstanceForSerial(
            bool startDeviceWatchers,
            List<string> portExclusionList = null,
            int bootTime = 1000)
        {
            return new PortSerialManager(
                startDeviceWatchers,
                portExclusionList,
                bootTime);
        }

        #endregion

        #region creating tcp/ip instances

        public static PortBase CreateInstanceForNetwork(bool startDeviceWatchers)
        {
            return new PortTcpIpManager(startDeviceWatchers);
        }

        public static PortBase CreateInstanceForNetwork(
            bool startDeviceWatchers,
            int discoveryPort)
        {
            return new PortTcpIpManager(
                startDeviceWatchers,
                discoveryPort);
        }

        #endregion

        public static PortBase CreateInstanceForComposite(
            IEnumerable<PortBase> ports,
            bool startDeviceWatchers)
        {
            return new PortCompositeDeviceManager(
                ports,
                startDeviceWatchers);
        }
    }
}
