//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger
{
    /////////////////////////////////////////////////////////////////////////////////////////////////////////
    // !!! KEEP IN SYNC WITH System.Net.NetworkInformation.Wireless80211 (in nanoFramework.System.Net) !!! //
    /////////////////////////////////////////////////////////////////////////////////////////////////////////

    public class Wireless80211Base
    {
        /// <summary>
        /// This is the marker placeholder for this configuration block
        /// 4 bytes length.
        /// </summary>
        public byte[] Marker;

        /// <summary>
        /// Id for the configuration
        /// </summary>
        public uint Id;
        
        /// <summary>
        /// Type of authentication used on the wireless network 
        /// </summary>
        public byte Authentication;

        /// <summary>
        /// Type of encryption used on the wireless network.
        /// </summary>
        public byte Encryption;

        /// <summary>
        /// Type of radio used by the wireless network adapter.
        /// </summary>
        public byte Radio;

        /// <summary>
        /// Network SSID
        /// </summary>
        public string Ssid;

        /// <summary>
        /// Network password 
        /// </summary>
        public string Password;
    }
}