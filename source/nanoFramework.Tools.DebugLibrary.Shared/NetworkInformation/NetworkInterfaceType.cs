//
// Copyright (c) 2018 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System.ComponentModel.DataAnnotations;

namespace nanoFramework.Tools.Debugger
{
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // !!! KEEP IN SYNC WITH System.Net.NetworkInformation.NetworkInterfaceType (in nanoFramework.System.Net) !!! //
    /////////////////////////////////////////////////////////////////////////////////////////////////////////////
    public enum NetworkInterfaceType : byte
    {
        /// <summary>
        /// The network interface type is unknown or not specified.
        /// </summary>
        Unknown = 1,

        /// <summary>
        /// The network interface uses an Ethernet connection. Ethernet is defined in IEEE standard 802.3.
        /// </summary>
        [Display(Description = "Ethernet")]
        Ethernet = 6,

        /// <summary>
        /// The network interface uses a wireless LAN connection (IEEE 802.11 standard).
        /// </summary>
        [Display(Description = "Wi-Fi (802.11)")]
        Wireless80211 = 71,
    }
}
