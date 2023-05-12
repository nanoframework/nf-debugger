//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System.ComponentModel;

namespace nanoFramework.Tools.Debugger
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // !!! KEEP IN SYNC WITH System.Net.NetworkInformation.EncryptionType (in nanoFramework.System.Net) !!! //
    //////////////////////////////////////////////////////////////////////////////////////////////////////////

    /// <summary>
    /// Encryption type for the Wireless network interface.
    /// </summary>
    public enum EncryptionType : byte
    {
        /// <summary>
        /// No encryption.
        /// </summary>
        [Description("")]
        None = 0,

        /// <summary>
        /// Wired Equivalent Privacy encryption.
        /// </summary>
        [Description("WEP")]
        WEP,

        /// <summary>
        /// Wireless Protected Access encryption.
        /// </summary>
        [Description("WPA")]
        WPA,

        /// <summary>
        /// Wireless Protected Access 2 encryption.
        /// </summary>
        [Description("WPA2")]
        WPA2,

        /// <summary>
        /// Wireless Protected Access Pre-Shared Key encryption.
        /// </summary>
        [Description("WPA Pre-Shared Key")]
        WPA_PSK,

        /// <summary>
        /// Wireless Protected Access 2 Pre-Shared Key encryption.
        /// </summary>
        [Description("WPA2 Pre-Shared Key")]
        WPA2_PSK2,

        /// <summary>
        /// Certificate encryption.
        /// </summary>
        [Description("Certificate")]
        Certificate,
    }
}
