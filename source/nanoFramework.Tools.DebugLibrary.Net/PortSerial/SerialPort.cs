//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.Serial;
using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using System.Windows;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    public partial class SerialPort : PortBase, IPort
    {
        /// <summary>
        /// Creates an Serial debug client
        /// </summary>
        public SerialPort(Application callerApp)
        {
            mapDeviceWatchersToDeviceSelector = new Dictionary<DeviceWatcher, String>();
            NanoFrameworkDevices = new ObservableCollection<NanoDeviceBase>();
            SerialDevices = new List<Serial.SerialDeviceInformation>();

            // set caller app property
            EventHandlerForSerialDevice.CallerApp = callerApp;

            // init semaphore
            semaphore = new SemaphoreSlim(1, 1);

            Task.Factory.StartNew(() =>
            {
                StartSerialDeviceWatchers();
            });
        }
    }
}
