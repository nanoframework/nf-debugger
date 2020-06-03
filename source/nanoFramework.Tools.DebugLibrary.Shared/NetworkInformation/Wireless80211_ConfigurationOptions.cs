//
// Copyright (c) 2020 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger
{
    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // !!! KEEP IN SYNC WITH System.Net.NetworkInformation.Wireless80211Configuration.ConfigurationOptions (in nanoFramework.System.Net) !!! //
    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    /// <summary>
    /// Configuration flags used for Wireless configuration.
    /// </summary>
    [Flags]
    public enum Wireless80211_ConfigurationOptions : byte
    {
        /// <summary>
        /// No flags set.
        /// </summary>
        None = 0,

        /// <summary>
        /// Enables the Wireless station.
        /// If not set the wireless station is disabled.
        /// </summary>
        Enable = 0x01,

        /// <summary>
        /// Will auto connect when AP is available or after being disconnected.
        /// </summary>
        AutoConnect = 0x02,

        /// <summary>
        /// Enables SmartConfig (if available) for this Wireless station
        /// </summary>
        SmartConfig = 0x04,
    };
}
