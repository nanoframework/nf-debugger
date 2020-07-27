//
// Copyright (c) 2018 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System.ComponentModel.DataAnnotations;

namespace nanoFramework.Tools.Debugger
{
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    // !!! KEEP IN SYNC WITH System.Net.NetworkInformation.EncryptionType (in nanoFramework.System.Net) !!! //
    //////////////////////////////////////////////////////////////////////////////////////////////////////////
    public enum EncryptionType : byte
    {
        /// <summary>
        /// No encryption.
        /// </summary>
        [Display(Description = "")]
        None = 0,

        /// <summary>
        /// Wired Equivalent Privacy encryption.
        /// </summary>
        [Display(Description = "WEP")]
        WEP,

        /// <summary>
        /// Wireless Protected Access encryption.
        /// </summary>
        [Display(Description = "WPA")]
        WPA,

        /// <summary>
        /// Wireless Protected Access 2 encryption.
        /// </summary>
        [Display(Description = "WPA2")]
        WPA2,

        /// <summary>
        /// Wireless Protected Access Pre-Shared Key encryption.
        /// </summary>
        [Display(Description = "WPA Pre-Shared Key")]
        WPA_PSK,

        /// <summary>
        /// Wireless Protected Access 2 Pre-Shared Key encryption.
        /// </summary>
        [Display(Description = "WPA2 Pre-Shared Key")]
        WPA2_PSK2,

        /// <summary>
        /// Certificate encryption.
        /// </summary>
        [Display(Description = "Certificate")]
        Certificate,
    }
}
