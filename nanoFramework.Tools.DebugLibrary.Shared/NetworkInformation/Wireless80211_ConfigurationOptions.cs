//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.ComponentModel;

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
        /// No option set.
        /// </summary>
        None = 0,

        /// <summary>
        /// Disables the Wireless station.
        /// </summary>
        Disable = 0x01,

        /// <summary>
        /// Enables the Wireless station.
        /// If not set the wireless station is disabled.
        /// </summary>
        Enable = 0x02,

        /// <summary>
        /// Will auto connect when AP is available or after being disconnected.
        /// This option forces enabling the Wireless station.
        /// </summary>
        [Description("Auto connect")]
        AutoConnect = 0x04 | Enable,

        /// <summary>
        /// Enables SmartConfig (if available) for this Wireless station.
        /// This option forces enabling the Wireless station.
        /// </summary>
        SmartConfig = 0x08 | Enable,
    };
}
