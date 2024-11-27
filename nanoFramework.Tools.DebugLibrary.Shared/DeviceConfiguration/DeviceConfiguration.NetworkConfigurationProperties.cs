//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Net;
using System.Text;

namespace nanoFramework.Tools.Debugger
{
    public partial class DeviceConfiguration
    {
        /////////////////////////////////////////////////////////////

        public partial class NetworkConfigurationProperties : NetworkConfigurationPropertiesBase
        {
            internal const uint EmptySpecificConfigValue = uint.MaxValue;

            public bool IsUnknown { get; set; }

            public NetworkConfigurationProperties() : base()
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
                AutomaticDNS = value.AutomaticDNS != 0;

                if (value.SpecificConfigId == EmptySpecificConfigValue)
                {
                    SpecificConfigId = null;
                }
                else
                {
                    SpecificConfigId = value.SpecificConfigId;
                }

                // reset unknown flag
                IsUnknown = false;
            }

            // operator to allow casting a NetworkConfigurationProperties object to NetworkConfigurationBase
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
                    StartupAddressMode = (byte)value.StartupAddressMode,
                };

                networkConfig.AutomaticDNS = value.AutomaticDNS ? (byte)1 : (byte)0;
                networkConfig.SpecificConfigId = value.SpecificConfigId ?? EmptySpecificConfigValue;

                return networkConfig;
            }
        }
    }
}
