//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;

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

        /////////////////////////////////////////////////////////////

        /// <summary>
        /// Collection of <see cref="NetworkConfigurationProperties"/> blocks in a target device.
        /// </summary>
        public List<NetworkConfigurationProperties> NetworkConfigurations { get; set; }

        /// <summary>
        /// Collection of <see cref="Wireless80211ConfigurationProperties"/> blocks in a target device.
        /// </summary>
        public List<Wireless80211ConfigurationProperties> Wireless80211Configurations { get; set; }

        public DeviceConfiguration()
            : this(new List<NetworkConfigurationProperties>(),
                   new List<Wireless80211ConfigurationProperties>())
        {
        }

        public DeviceConfiguration(
            List<NetworkConfigurationProperties> networkConfiguratons,
            List<Wireless80211ConfigurationProperties> networkWirelessConfiguratons
            )
        {
            NetworkConfigurations = networkConfiguratons;
            Wireless80211Configurations = networkWirelessConfiguratons;
        }

        // operator to allow cast_ing a DeviceConfiguration object to DeviceConfigurationBase
        public static explicit operator DeviceConfigurationBase(DeviceConfiguration value)
        {
            return new DeviceConfigurationBase()
            {
                NetworkConfigurations = value.NetworkConfigurations.Select(i => (NetworkConfigurationBase)i).ToArray(),
                Wireless80211Configurations = value.Wireless80211Configurations.Select(i => (Wireless80211ConfigurationBase)i).ToArray()
            };
        }

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
                var addressAsArray = address.GetAddressBytes();

                return (((uint)addressAsArray[3] << 24) | 
                        ((uint)addressAsArray[2] << 16) | 
                        ((uint)addressAsArray[1] << 8) | 
                        (addressAsArray[0]));
            }
            catch { };

            return 0;
        }

        static internal uint[] FromIPv6Address(IPAddress address)
        {
            try
            {
                var addressAsArray = address.ToString().Split(new string[] { ":", "::" }, StringSplitOptions.RemoveEmptyEntries);

                return new uint[] { uint.Parse(addressAsArray[3], NumberStyles.HexNumber),
                                    uint.Parse(addressAsArray[2], NumberStyles.HexNumber),
                                    uint.Parse(addressAsArray[1], NumberStyles.HexNumber),
                                    uint.Parse(addressAsArray[0], NumberStyles.HexNumber) };
            }
            catch { };

            return new uint[4];
        }

        /////////////////////////////////////////////////////////////

        public class NetworkConfigurationProperties : NetworkConfigurationPropertiesBase
        {
            internal const uint EmptySpecificConfigValue = uint.MaxValue;

            public bool IsUnknown { get; set; } = true;

            public NetworkConfigurationProperties()
            {

            }

            public NetworkConfigurationProperties(NetworkConfigurationBase value)
            {
                MacAddress = value.MacAddress;

                IPv4Address = new IPAddress(value.IPv4Address);
                IPv4NetMask = new IPAddress(value.IPv4NetMask);
                IPv4GatewayAddress = new IPAddress(value.IPv4GatewayAddress);
                IPv4DNSAddress1 = new IPAddress(value.IPv4DNSAddress1);
                IPv4DNSAddress2 = new IPAddress(value.IPv4DNSAddress2);

                IPv6Address = ToIPv6Address(value.IPv6Address);
                IPv6NetMask = ToIPv6Address(value.IPv6NetMask);
                IPv6GatewayAddress = ToIPv6Address(value.IPv6GatewayAddress);
                IPv6DNSAddress1 = ToIPv6Address(value.IPv6DNSAddress1);
                IPv6DNSAddress2 = ToIPv6Address(value.IPv6DNSAddress2);

                InterfaceType = (NetworkInterfaceType)value.InterfaceType;
                StartupAddressMode = (AddressMode)value.StartupAddressMode;

                if (value.SpecificConfigId == EmptySpecificConfigValue)
                {
                    SpecificConfigId =  null;
                }
                else
                {
                    SpecificConfigId =  value.SpecificConfigId;
                }

                // reset unknown flag
                IsUnknown = false;
            }

            // operator to allow cast_ing a NetworkConfigurationProperties object to NetworkConfigurationBase
            public static explicit operator NetworkConfigurationBase(NetworkConfigurationProperties value)
            {
                var networkConfig = new NetworkConfigurationBase()
                {
                    Marker = Encoding.UTF8.GetBytes(MarkerConfigurationNetwork_v1),

                    MacAddress = value.MacAddress,

                    IPv4Address = FromIPv4Address(value.IPv4Address),
                    IPv4NetMask = FromIPv4Address(value.IPv4NetMask),
                    IPv4GatewayAddress = FromIPv4Address(value.IPv4GatewayAddress),
                    IPv4DNSAddress1 = FromIPv4Address(value.IPv4DNSAddress1),
                    IPv4DNSAddress2 = FromIPv4Address(value.IPv4DNSAddress2),

                    IPv6Address = FromIPv6Address(value.IPv6Address),
                    IPv6NetMask = FromIPv6Address(value.IPv6NetMask),
                    IPv6GatewayAddress = FromIPv6Address(value.IPv6GatewayAddress),
                    IPv6DNSAddress1 = FromIPv6Address(value.IPv6DNSAddress1),
                    IPv6DNSAddress2 = FromIPv6Address(value.IPv6DNSAddress2),
                    InterfaceType = (byte)value.InterfaceType,
                    StartupAddressMode = (byte)value.StartupAddressMode
                };

                networkConfig.SpecificConfigId = value.SpecificConfigId ?? EmptySpecificConfigValue;

                return networkConfig;
            }

        }

        public class Wireless80211ConfigurationProperties : Wireless80211ConfigurationPropertiesBase
        {
            public bool IsUnknown { get; set; } = true;

            public Wireless80211ConfigurationProperties()
            {

            }

            public Wireless80211ConfigurationProperties(Wireless80211ConfigurationBase config)
            {
                Id = config.Id;
                Authentication = (AuthenticationType)config.Authentication;
                Encryption = (EncryptionType)config.Encryption;
                Radio = (RadioType)config.Radio;
                Ssid = config.Ssid;
                Password = config.Password;

                // reset unknown flag
                IsUnknown = false;
            }

            // operator to allow cast_ing a NetworkWirelessConfigurationProperties object to NetworkConfigurationBase
            public static explicit operator Wireless80211ConfigurationBase(Wireless80211ConfigurationProperties value)
            {
                var networkWirelessConfig = new Wireless80211ConfigurationBase()
                {
                    Marker = Encoding.UTF8.GetBytes(MarkerConfigurationWireless80211_v1),

                    Id = value.Id,

                    Authentication = (byte)value.Authentication,
                    Encryption = (byte)value.Encryption,
                    Radio = (byte)value.Radio,
                    Ssid = value.Ssid,
                    Password = value.Password,
            };

                return networkWirelessConfig;
            }

        }

        /////////////////////////////////////////////////////////////

    }
}
