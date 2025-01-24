//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System.ComponentModel;

namespace nanoFramework.Tools.Debugger
{
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // !!! KEEP IN SYNC WITH System.Net.NetworkInformation.NetworkInterfaceType (in nanoFramework.System.Net) !!! //
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    /// <summary>
    /// Network interface type.
    /// </summary>
    public enum NetworkInterfaceType : byte
    {
        /// <summary>
        /// The network interface type is unknown or not specified.
        /// </summary>
        [Description("")]
        Unknown = 1,

        /// <summary>
        /// The network interface uses an Ethernet connection. Ethernet is defined in IEEE standard 802.3.
        /// </summary>
        [Description("Ethernet")]
        Ethernet = 6,

        /// <summary>
        /// The network interface uses a wireless LAN connection (IEEE 802.11 standard).
        /// </summary>
        [Description("Wi-Fi (802.11)")]
        Wireless80211 = 71,


        /// <summary>
        /// The network interface uses a wireless Soft AP connection (IEEE 802.11 standard).
        /// </summary>
        [Description("Wi-Fi Access Point (802.11)")]
        WirelessAP = 72,
    }
}
