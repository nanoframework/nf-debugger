//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
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
        /// Network configuration market
        /// </summary>
        public static string MarkerConfigurationNetwork_v1 = "CN1\0";
        /////////////////////////////////////////////////////////////

        public class NetworkConfigurationProperties
        {
            /// <summary>
            /// MAC address for the network interface
            /// </summary>
            public byte[] MacAddress { get; set; }

            /// <summary>
            /// Network IPv4 address 
            /// </summary>
            public IPAddress IPv4Address { get; set; }

            /// <summary>
            /// Network IPv4 subnet mask
            /// </summary>
            public IPAddress IPv4NetMask { get; set; }

            /// <summary>
            /// Network gateway IPv4 address
            /// </summary>
            public IPAddress IPv4GatewayAddress { get; set; }

            /// <summary>
            /// DNS server 1 IPv4 address
            /// </summary>
            public IPAddress IPv4DNS1Address { get; set; }

            /// <summary>
            /// DNS server 2 IPv4 address
            /// </summary>
            public IPAddress IPv4DNS2Address { get; set; }

            /// <summary>
            /// Network IPv6 address 
            /// </summary>
            public IPAddress IPv6Address { get; set; }

            // Network IPv6 subnet mask
            public IPAddress IPv6NetMask { get; set; }

            // Network gateway IPv6 address
            public IPAddress IPv6GatewayAddress { get; set; }

            // DNS server 1 IPv6 address
            public IPAddress IPv6DNS1Address { get; set; }

            // DNS server 2 IPv6 address
            public IPAddress IPv6DNS2Address { get; set; }

            /// <summary>
            /// Address mode (static, DHCP or auto IP)
            /// </summary>
            public AddressMode StartupAddressMode { get; set; }

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
                IPv4DNS1Address = new IPAddress(ipv4DNS1Address);
                IPv4DNS2Address = new IPAddress(ipv4DNS2Address);

                IPv6Address = ToIPv6Address(ipv6Address);
                IPv6NetMask = ToIPv6Address(ipv6NetMask);
                IPv6GatewayAddress = ToIPv6Address(ipv6GatewayAddress);
                IPv6DNS1Address = ToIPv6Address(ipv6DNS1Address);
                IPv6DNS2Address = ToIPv6Address(ipv6DNS2Address);

                StartupAddressMode = (AddressMode)startupAddressMode;
            }

            // operator to allow cast_ing a NetworkConfigurationProperties object to NetworkConfigurationBase
            public static explicit operator NetworkConfigurationBase(NetworkConfigurationProperties value)
            {
                var networkCondif = new NetworkConfigurationBase()
                {
                    Marker = Encoding.UTF8.GetBytes(MarkerConfigurationNetwork_v1),

                    MacAddress = value.MacAddress,

                    IPv4Address = FromIPv4Address(value.IPv4Address),
                    IPv4NetMask = FromIPv4Address(value.IPv4NetMask),
                    IPv4GatewayAddress = FromIPv4Address(value.IPv4GatewayAddress),
                    IPv4DNS1Address = FromIPv4Address(value.IPv4DNS1Address),
                    IPv4DNS2Address = FromIPv4Address(value.IPv4DNS2Address),

                    IPv6Address = FromIPv6Address(value.IPv6Address),
                    IPv6NetMask = FromIPv6Address(value.IPv6NetMask),
                    IPv6GatewayAddress = FromIPv6Address(value.IPv6GatewayAddress),
                    IPv6DNS1Address = FromIPv6Address(value.IPv6DNS1Address),
                    IPv6DNS2Address = FromIPv6Address(value.IPv6DNS2Address),

                    StartupAddressMode = (byte)value.StartupAddressMode
                };

                return networkCondif;
            }

        }

        private NetworkConfigurationProperties _networkConfiguration;

        public NetworkConfigurationProperties NetworkConfiguraton
        {
            get
            {
                Debug.Assert(!IsUnknown);
                return _networkConfiguration;
            }

            set { _networkConfiguration = value; }
        }

        public bool IsUnknown => false;

        public DeviceConfiguration()
            : this(new NetworkConfigurationProperties())
        {
        }

        public DeviceConfiguration(
            NetworkConfigurationProperties networkConfiguraton
            )
        {
            _networkConfiguration = networkConfiguraton;
        }

        // operator to allow cast_ing a DeviceConfiguration object to DeviceConfigurationBase
        public static explicit operator DeviceConfigurationBase(DeviceConfiguration value)
        {
            return new DeviceConfigurationBase()
            {
                NetworkConfiguration = (NetworkConfigurationBase)value.NetworkConfiguraton
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

                return (((uint)addressAsArray[0] << 24) | 
                        ((uint)addressAsArray[1] << 16) | 
                        ((uint)addressAsArray[2] << 8) | 
                        (addressAsArray[3]));
            }
            catch { };

            return 0;
        }

        static internal uint[] FromIPv6Address(IPAddress address)
        {
            try
            {
                var addressAsArray = address.ToString().Split(new string[] { ":", "::" }, StringSplitOptions.RemoveEmptyEntries);

                return new uint[] { uint.Parse(addressAsArray[0], System.Globalization.NumberStyles.HexNumber),
                                    uint.Parse(addressAsArray[1], System.Globalization.NumberStyles.HexNumber),
                                    uint.Parse(addressAsArray[2], System.Globalization.NumberStyles.HexNumber),
                                    uint.Parse(addressAsArray[3], System.Globalization.NumberStyles.HexNumber) };
            }
            catch { };

            return new uint[4];
        }
    }
}
