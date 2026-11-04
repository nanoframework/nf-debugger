//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Linq;
using System.Net;

namespace nanoFramework.Tools.Debugger
{
    public partial class DeviceConfiguration
    {
        /////////////////////////////////////////////////////////////
        // configuration block markers
        // all markers have 4 bytes length

        /// <summary>
        /// Network configuration marker
        /// </summary>
        public static string MarkerConfigurationNetwork_v1 = "CN1\0";

        /// <summary>
        /// Wireless network configuration marker
        /// </summary>
        public static string MarkerConfigurationWireless80211_v1 = "WN1\0";

        /// <summary>
        /// Wireless AP configuration marker
        /// </summary>
        public static string MarkerConfigurationWireless80211AP_v1 = "AP1\0";

        /// <summary>
        /// X509 CA Root bundle configuration marker
        /// </summary>
        public static string MarkerConfigurationX509CaRootBundle_v1 = "XB1\0";

        /// <summary>
        /// X509 Device certificate configuration marker
        /// </summary>
        public static string MarkerConfigurationX509DeviceCertificate_v1 = "XD1\0";

        /// <summary>
        /// Wire Protocol configuration marker
        /// </summary>
        public static string MarkerConfigurationWireProtocol_v1 = "WP1\0";

        /// <summary>
        /// Maximum length, in bytes, of the Wire Protocol authentication token.
        /// KEEP IN SYNC WITH native WIREPROTOCOL_CONFIG_MAX_TOKEN_LENGTH in nanoHAL_Network.h.
        /// </summary>
        public const int WireProtocolMaxTokenLength = 64;

        /////////////////////////////////////////////////////////////

        /// <summary>
        /// Id to set in a wireless profile being added, so the device assigns one.
        /// </summary>
        public const uint ConfigurationBlockIdAuto = 0xFFFFFFFF;

        /// <summary>
        /// Converts an array of four uint values to it's equivalent IPv6 address.
        /// </summary>
        /// <param name="buffer"></param>
        /// <returns></returns>
        static internal IPAddress ToIPv6Address(uint[] buffer)
        {
            try
            {
                return IPAddress.Parse($"{buffer[0].ToString("x4")}:{buffer[1].ToString("x4")}:{buffer[2].ToString("x4")}:{buffer[3].ToString("x4")}");
            }
            catch { };

            return IPAddress.Parse("ffff::ffff:ffff:ffff");
        }

        static internal uint FromIPv4Address(IPAddress address)
        {
            try
            {
                if (address != null)
                {
                    var addressAsArray = address.GetAddressBytes();

                    return (((uint)addressAsArray[3] << 24) |
                            ((uint)addressAsArray[2] << 16) |
                            ((uint)addressAsArray[1] << 8) |
                            (addressAsArray[0]));
                }
            }
            catch { };

            return 0;
        }

        static internal uint[] FromIPv6Address(IPAddress address)
        {
            try
            {
                if (address != null)
                {
                    var addressBytes = address.GetAddressBytes();
                    var addressBytesReversed = System.Linq.Enumerable.Reverse(addressBytes).ToArray();

                    return new uint[] { BitConverter.ToUInt32(addressBytesReversed, 0),
                                    BitConverter.ToUInt32(addressBytesReversed, 4),
                                    BitConverter.ToUInt32(addressBytesReversed, 8),
                                    BitConverter.ToUInt32(addressBytesReversed, 12) };
                }
            }
            catch { };

            return new uint[4];
        }

        /////////////////////////////////////////////////////////////
    }
}
