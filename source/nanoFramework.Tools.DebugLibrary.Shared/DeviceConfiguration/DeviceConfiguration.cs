//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
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
        public static string MarkerConfigurationWireless80211Network_v1 = "WN1\0";

        /// <summary>
        /// Wireless AP configuration marker
        /// </summary>
        public static string MarkerConfigurationWireless80211AP_v1 = "AP1\0";

        /////////////////////////////////////////////////////////////

        private NetworkConfigurationProperties[] _networkConfigurations;

        /// <summary>
        /// Collection of <see cref="NetworkConfigurationProperties"/> blocks in a target device.
        /// </summary>
        public NetworkConfigurationProperties[] NetworkConfigurations
        {
            get
            {
                Debug.Assert(!IsUnknown);
                return _networkConfigurations;
            }

            set { _networkConfigurations = value; }
        }

        private NetworkWireless80211ConfigurationProperties[] _networkWirelessConfigurations;

        /// <summary>
        /// Collection of <see cref="NetworkWireless80211ConfigurationProperties"/> blocks in a target device.
        /// </summary>
        public NetworkWireless80211ConfigurationProperties[] NetworkWirelessConfigurations
        {
            get
            {
                Debug.Assert(!IsUnknown);
                return _networkWirelessConfigurations;
            }

            set { _networkWirelessConfigurations = value; }
        }

        public bool IsUnknown => false;

        public DeviceConfiguration()
            : this(new NetworkConfigurationProperties[0],
                   new NetworkWireless80211ConfigurationProperties[0])
        {
        }

        public DeviceConfiguration(
            NetworkConfigurationProperties[] networkConfiguratons,
            NetworkWireless80211ConfigurationProperties[] networkWirelessConfiguratons
            )
        {
            _networkConfigurations = networkConfiguratons;
            _networkWirelessConfigurations = networkWirelessConfiguratons;
        }

        // operator to allow cast_ing a DeviceConfiguration object to DeviceConfigurationBase
        public static explicit operator DeviceConfigurationBase(DeviceConfiguration value)
        {
            return new DeviceConfigurationBase()
            {
                NetworkConfigurations = value.NetworkConfigurations.Select(i => (NetworkConfigurationBase)i).ToArray(),
                NetworkWirelessConfigurations = value.NetworkWirelessConfigurations.Select(i => (NetworkWireless80211ConfigurationBase)i).ToArray()
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

                return new uint[] { uint.Parse(addressAsArray[3], System.Globalization.NumberStyles.HexNumber),
                                    uint.Parse(addressAsArray[2], System.Globalization.NumberStyles.HexNumber),
                                    uint.Parse(addressAsArray[1], System.Globalization.NumberStyles.HexNumber),
                                    uint.Parse(addressAsArray[0], System.Globalization.NumberStyles.HexNumber) };
            }
            catch { };

            return new uint[4];
        }

        /////////////////////////////////////////////////////////////

        public class NetworkConfigurationProperties : NetworkConfigurationPropertiesBase
        {
            public NetworkConfigurationProperties()
            {

            }

            public NetworkConfigurationProperties(
                byte[] macAddress,
                uint ipv4Address,
                uint ipv4NetMask,
                uint ipv4GatewayAddress,
                uint ipv4DNS1Address,
                uint ipv4DNS2Address,
                uint[] ipv6Address,
                uint[] ipv6NetMask,
                uint[] ipv6GatewayAddress,
                uint[] ipv6DNS1Address,
                uint[] ipv6DNS2Address,
                byte startupAddressMode)
            {
                MacAddress = macAddress;

                IPv4Address = new IPAddress(ipv4Address);
                IPv4NetMask = new IPAddress(ipv4NetMask);
                IPv4GatewayAddress = new IPAddress(ipv4GatewayAddress);
                IPv4DNSAddress1 = new IPAddress(ipv4DNS1Address);
                IPv4DNSAddress2 = new IPAddress(ipv4DNS2Address);

                IPv6Address = ToIPv6Address(ipv6Address);
                IPv6NetMask = ToIPv6Address(ipv6NetMask);
                IPv6GatewayAddress = ToIPv6Address(ipv6GatewayAddress);
                IPv6DNSAddress1 = ToIPv6Address(ipv6DNS1Address);
                IPv6DNSAddress2 = ToIPv6Address(ipv6DNS2Address);

                StartupAddressMode = (AddressMode)startupAddressMode;
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

                    StartupAddressMode = (byte)value.StartupAddressMode
                };

                return networkConfig;
            }

        }

        public class NetworkWireless80211ConfigurationProperties : NetworkWireless80211ConfigurationPropertiesBase
        {
            public NetworkWireless80211ConfigurationProperties()
            {

            }

            public NetworkWireless80211ConfigurationProperties(
                byte[] macAddress,
                uint ipv4Address,
                uint ipv4NetMask,
                uint ipv4GatewayAddress,
                uint ipv4DNS1Address,
                uint ipv4DNS2Address,
                uint[] ipv6Address,
                uint[] ipv6NetMask,
                uint[] ipv6GatewayAddress,
                uint[] ipv6DNS1Address,
                uint[] ipv6DNS2Address,
                byte startupAddressMode,
                byte authentication,
                byte encryption,
                byte radio,
                string ssid,
                string password)
            {
                MacAddress = macAddress;

                IPv4Address = new IPAddress(ipv4Address);
                IPv4NetMask = new IPAddress(ipv4NetMask);
                IPv4GatewayAddress = new IPAddress(ipv4GatewayAddress);
                IPv4DNSAddress1 = new IPAddress(ipv4DNS1Address);
                IPv4DNSAddress2 = new IPAddress(ipv4DNS2Address);

                IPv6Address = ToIPv6Address(ipv6Address);
                IPv6NetMask = ToIPv6Address(ipv6NetMask);
                IPv6GatewayAddress = ToIPv6Address(ipv6GatewayAddress);
                IPv6DNSAddress1 = ToIPv6Address(ipv6DNS1Address);
                IPv6DNSAddress2 = ToIPv6Address(ipv6DNS2Address);

                StartupAddressMode = (AddressMode)startupAddressMode;

                Authentication = (AuthenticationType)authentication;
                Encryption = (EncryptionType)encryption;
                Radio = (RadioType)radio;
                Ssid = ssid;
                Password = password;

            }

            // operator to allow cast_ing a NetworkWirelessConfigurationProperties object to NetworkConfigurationBase
            public static explicit operator NetworkWireless80211ConfigurationBase(NetworkWireless80211ConfigurationProperties value)
            {
                var networkWirelessConfig = new NetworkWireless80211ConfigurationBase()
                {
                    Marker = Encoding.UTF8.GetBytes(MarkerConfigurationWireless80211Network_v1),

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

                    StartupAddressMode = (byte)value.StartupAddressMode,

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
