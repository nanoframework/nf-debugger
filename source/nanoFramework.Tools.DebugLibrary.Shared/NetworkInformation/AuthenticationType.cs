//
// Copyright (c) 2018 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace System.Net.NetworkInformation
{
    /// <summary>
    /// Specifies the authentication used in a wireless network.
    /// </summary>
    public enum AuthenticationType : byte
    {
        /// <summary>
        /// No protocol.
        /// </summary>
        None = 0,
        /// <summary>
        /// Extensible Authentication Protocol.
        /// </summary>
        EAP,
        /// <summary>
        /// Protected Extensible Authentication Protocol.
        /// </summary>
        PEAP,
        /// <summary>
        /// Microsoft Windows Connect Now protocol.
        /// </summary>
        WCN,
        /// <summary>
        /// Open System authentication, for use with WEP encryption type.
        /// </summary>
        Open,
        /// <summary>
        /// Shared Key authentication, for use with WEP encryption type.
        /// </summary>
        Shared,
        /// <summary>
        /// Wired Equivalent Privacy protocol.
        /// </summary>
        WEP,
        /// <summary>
        /// Wi-Fi Protected Access protocol.
        /// </summary>
        WPA,
        /// <summary>
        /// Wi-Fi Protected Access 2 protocol.
        /// </summary>
        WPA2,
    }
}
