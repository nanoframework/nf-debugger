//
// Copyright (c) 2018 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger
{
    /////////////////////////////////////////////////////////////////////////////////////////////////////
    // !!! KEEP IN SYNC WITH System.Net.NetworkInformation.RadioType (in nanoFramework.System.Net) !!! //
    /////////////////////////////////////////////////////////////////////////////////////////////////////
    public enum RadioType : byte
    {
        /// <summary>
        /// Not defined.
        /// </summary>
        None = 0,
        /// <summary>
        /// 802.11a-compatible radio.
        /// </summary>
        _802_11a = 1,
        /// <summary>
        /// 802.11b-compatible radio.
        /// </summary>
        _802_11b = 2,
        /// <summary>
        /// 802.11g-compatible radio.
        /// </summary>
        _802_11g = 4,
        /// <summary>
        /// 802.11n-compatible radio.
        /// </summary>
        _802_11n = 8,
    }
}
