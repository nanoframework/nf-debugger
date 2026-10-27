// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace nanoFramework.Tools.Debugger
{
    /// <summary>
    /// Data for the <see cref="NanoDeviceCollection.DeviceAdded"/> and <see cref="NanoDeviceCollection.DeviceRemoved"/> events.
    /// </summary>
    public sealed class NanoDeviceEventArgs : EventArgs
    {
        /// <summary>
        /// The device that has been added or removed.
        /// </summary>
        public NanoDeviceBase Device { get; }

        /// <summary>
        /// Creates the event data for <paramref name="device"/>.
        /// </summary>
        /// <param name="device">The device that has been added or removed.</param>
        public NanoDeviceEventArgs(NanoDeviceBase device)
        {
            Device = device;
        }
    }
}
