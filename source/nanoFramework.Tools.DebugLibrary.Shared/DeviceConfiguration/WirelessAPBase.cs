//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger
{
    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////
    // !!! KEEP IN SYNC WITH System.Net.NetworkInformation.WirelessAPConfiguration (in nanoFramework.System.Net) !!! //
    ///////////////////////////////////////////////////////////////////////////////////////////////////////////////////

    public class WirelessAPConfigurationBase
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
        /// 32 bytes length.
        /// </summary>
        public byte[] Ssid;

        /// <summary>
        /// Network password 
        /// 64 bytes length.
        /// </summary>
        public byte[] Password;

        /// <summary>
        /// Options
        /// 1 byte length.
        /// </summary>
        public byte Options;

        /// <summary>
        /// Channel
        /// 1 byte length.
        /// </summary>
        public byte Channel;

        /// <summary>
        /// Max connections
        /// 1 byte length.
        /// </summary>
        public byte MaxConnections;

        public WirelessAPConfigurationBase()
        {
            // need to init these here to match the expected size on the struct to be sent to the device
            Marker = new byte[4];
            Ssid = new byte[32];
            Password = new byte[64];
        }
    }
}