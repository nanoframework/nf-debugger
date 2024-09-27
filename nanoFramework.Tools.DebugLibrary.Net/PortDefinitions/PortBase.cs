// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using nanoFramework.Tools.Debugger.PortComposite;
using nanoFramework.Tools.Debugger.PortSerial;
using nanoFramework.Tools.Debugger.PortTcpIp;

namespace nanoFramework.Tools.Debugger
{
    //  write intellisense documentation for this class  
    public abstract partial class PortBase : PortMessageBase
    {
        protected PortBase()
        {
            NanoFrameworkDevices = NanoFrameworkDevices.Instance;
        }

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
