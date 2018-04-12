//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger
{
    public class NetworkWireless80211ConfigurationBase : NetworkConfigurationBase
    {
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