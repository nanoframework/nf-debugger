//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

namespace System.Net.NetworkInformation
{
    /// <summary>
    ///  Start up network IP address assigning modes
    /// </summary>
    /// <remarks>
    /// This Enum is exclusive of nanoFramework and it does not exist on the UWP API.
    /// </remarks>
    public enum AddressMode : byte
    {
        ////////////////////////////////////////////////////////////////////////////////////
        // NEED TO KEEP THESE IN SYNC WITH native 'AddressMode' enum in nanoHAL_Network.h //
        ////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Invalid state.
        /// </summary>
        Invalid = 0,

        /// <summary>
        /// IP address from DHCP.
        /// </summary>
        DHCP = 1,

        /// <summary>
        /// Static IP address.
        /// </summary>
        Static = 2,

        /// <summary>
        /// Auto IP.
        /// </summary>
        AutoIP = 3,
    }
}
