//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using NanoFramework.Tools.Debugger.PortSerial;
using NanoFramework.Tools.Debugger.Usb;
using System;
using System.Collections.ObjectModel;
using Windows.UI.Xaml;

namespace NanoFramework.Tools.Debugger
{
    public abstract class PortBase
    {
        public override bool Equals(object obj)
        {
            PortBase pd = obj as PortBase; if (pd == null) return false;

            return (pd.UniqueId.Equals(UniqueId));
        }

        public override int GetHashCode()
        {
            return base.GetHashCode();
        }

        public string PortName { get; internal set; }

        public virtual object UniqueId
        {
            get
            {
                return PortName;
            }
        }

        public string PersistName
        {
            get
            {
                return UniqueId.ToString();
            }
        }

        /// <summary>
        /// Event that is raised when enumeration of all nF devices is complete.
        /// </summary>
        public abstract event EventHandler DeviceEnumerationCompleted;

        public ObservableCollection<NanoDeviceBase> NanoFrameworkDevices { get; protected set; }

        public static PortBase CreateInstanceForUsb(string displayName, Application callerApp)
        {
            return new UsbPort(callerApp);
        }

        public static PortBase CreateInstanceForSerial(string displayName, Application callerApp)
        {
            return new SerialPort(callerApp);
        }
    }
}
