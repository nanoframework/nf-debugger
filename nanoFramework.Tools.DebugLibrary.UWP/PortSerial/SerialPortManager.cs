//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.UI.Xaml;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    public partial class SerialPortManager : PortBase
    {
        /// <summary>
        /// Creates an Serial debug client
        /// </summary>
        public SerialPortManager(Application callerApp, bool startDeviceWatchers = true)
        {
            _mapDeviceWatchersToDeviceSelector = new Dictionary<DeviceWatcher, String>();
            NanoFrameworkDevices = new ObservableCollection<NanoDeviceBase>();

            // set caller app property
            CallerApp = callerApp;

            Task.Factory.StartNew(() =>
            {
                if (startDeviceWatchers)
                {
                    StartSerialDeviceWatchers();
                }
            });
        }
    }
}
