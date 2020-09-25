//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace nanoFramework.Tools.Debugger
{
    public abstract partial class PortBase
    {
        public List<string> PortBlackList { get; set; } = new List<string>();

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

        /// <summary>
        /// Flag to signal that devices enumeration is complete.
        /// </summary>
        public bool IsDevicesEnumerationComplete { get; internal set; } = false;

        public ObservableCollection<NanoDeviceBase> NanoFrameworkDevices { get; protected set; }

        /// <summary>
        /// Starts the device watchers.
        /// If they are already started this operation won't have any effect.
        /// </summary>
        public abstract void StartDeviceWatchers();

        /// <summary>
        /// Stops the device watchers.
        /// If they are already stopped this operation won't have any effect.
        /// </summary>
        public abstract void StopDeviceWatchers();

        /// <summary>
        /// Performs a re-scan of the connected devices.
        /// This operation resets the list of available devices and attempts to validate if a connected device it's a nanoDevice.
        /// </summary>
        public abstract void ReScanDevices();
    }
}
