//
// Copyright (c) 2020 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger
{
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // !!! KEEP IN SYNC WITH System.Net.NetworkInformation.WirelessAPConfiguration.ConfigurationOptions (in nanoFramework.System.Net) !!! //
    ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    /// <summary>
    /// Configuration flags used for Wireless Soft AP configuration.
    /// </summary>
    [Flags]
    public enum WirelessAP_ConfigurationOptions : byte
    {
        /// <summary>
        /// No flags set.
        /// </summary>
        None = 0,

        /// <summary>
        /// Enables the Wireless Soft AP.
        /// If not set the wireless Soft AP is disabled.
        /// </summary>
        Enable = 0x01,

        /// <summary>
        /// Will automatically start AP when CLR starts.
        /// </summary>
        AutoStart = 0x02,

        /// <summary>
        /// The SSID for the Soft AP will be hidden 
        /// </summary>
        HiddenSSID = 0x04,
    };
}
