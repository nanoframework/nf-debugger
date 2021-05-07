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
    }
}
