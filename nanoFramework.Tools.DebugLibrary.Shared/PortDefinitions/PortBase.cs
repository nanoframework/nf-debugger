﻿//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;

namespace nanoFramework.Tools.Debugger
{
    public abstract partial class PortBase
    {
        public List<string> PortExclusionList { get; set; } = new List<string>();

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

        public NanoFrameworkDevices NanoFrameworkDevices { get; protected set; }

        /// <summary>
        /// Adds a new <see cref="PortSerial"/> device to list of NanoFrameworkDevices.
        /// </summary>
        /// <param name="deviceId">The serial port name where the device is connected.</param>
        public abstract void AddDevice(string deviceId);

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

        /// <summary>
        /// Disposes a device.
        /// </summary>
        /// <param name="instanceId">Id of the device to dispose.</param>
        public abstract void DisposeDevice(string instanceId);
    }
}
