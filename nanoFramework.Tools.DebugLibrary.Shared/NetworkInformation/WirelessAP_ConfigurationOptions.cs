//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.ComponentModel;

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
        /// No option set.
        /// </summary>
        None = 0,

        /// <summary>
        /// Disables the Wireless Soft AP.
        /// </summary>
        Disable = 0x01,

        /// <summary>
        /// Enables the Wireless Soft AP.
        /// If not set the Wireless Soft AP is disabled.
        /// </summary>
        Enable = 0x02,

        /// <summary>
        /// Will automatically start the Soft AP when CLR starts.
        /// This option forces enabling the Wireless Soft AP.
        /// </summary>
        [Description("Auto start")]
        AutoStart = 0x04 | Enable,

        /// <summary>
        /// The SSID for the Soft AP will be hidden.
        /// </summary>
        [Description("SSID hidden")]
        HiddenSSID = 0x08,
    };
}
