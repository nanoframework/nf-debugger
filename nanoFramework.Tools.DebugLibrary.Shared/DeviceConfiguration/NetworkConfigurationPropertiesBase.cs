//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using CommunityToolkit.Mvvm.ComponentModel;
using System.Net;

namespace nanoFramework.Tools.Debugger
{
    public class NetworkConfigurationPropertiesBase : ObservableObject
    {
        private IPAddress _ipv4Address;
        private IPAddress _ipv4DNSAddress1;
        private IPAddress _ipv4DNSAddress2;
        private IPAddress _ipv4GatewayAddress;
        private IPAddress _ipv4NetMask;
        private IPAddress _ipv6Address;
        private IPAddress _ipv6DNSAddress1;
        private IPAddress _ipv6DNSAddress2;
        private IPAddress _ipv6GatewayAddress;
        private IPAddress _ipv6NetMask;
        private byte[] _macAddress;
        private NetworkInterfaceType _interfaceType;
        private AddressMode _startupAddressMode;
        private uint? _specificConfigId;
        private bool _automaticDNS;

        public IPAddress IPv4Address
        {
            get => _ipv4Address;
            set => SetProperty(ref _ipv4Address, value);
        }

        public IPAddress IPv4DNSAddress1
        {
            get => _ipv4DNSAddress1;
            set => SetProperty(ref _ipv4DNSAddress1, value);
        }

        public IPAddress IPv4DNSAddress2
        {
            get => _ipv4DNSAddress2;
            set => SetProperty(ref _ipv4DNSAddress2, value);
        }

        public IPAddress IPv4GatewayAddress
        {
            get => _ipv4GatewayAddress;
            set => SetProperty(ref _ipv4GatewayAddress, value);
        }

        public IPAddress IPv4NetMask
        {
            get => _ipv4NetMask;
            set => SetProperty(ref _ipv4NetMask, value);
        }

        public IPAddress IPv6Address
        {
            get => _ipv6Address;
            set => SetProperty(ref _ipv6Address, value);
        }

        public IPAddress IPv6DNSAddress1
        {
            get => _ipv6DNSAddress1;
            set => SetProperty(ref _ipv6DNSAddress1, value);
        }

        public IPAddress IPv6DNSAddress2
        {
            get => _ipv6DNSAddress2;
            set => SetProperty(ref _ipv6DNSAddress2, value);
        }

        public IPAddress IPv6GatewayAddress
        {
            get => _ipv6GatewayAddress;
            set => SetProperty(ref _ipv6GatewayAddress, value);
        }

        public IPAddress IPv6NetMask
        {
            get => _ipv6NetMask;
            set => SetProperty(ref _ipv6NetMask, value);
        }

        public byte[] MacAddress
        {
            get => _macAddress;
            set => SetProperty(ref _macAddress, value);
        }

        public NetworkInterfaceType InterfaceType
        {
            get => _interfaceType;
            set => SetProperty(ref _interfaceType, value);
        }

        public AddressMode StartupAddressMode
        {
            get => _startupAddressMode;
            set => SetProperty(ref _startupAddressMode, value);
        }

        public uint? SpecificConfigId
        {
            get => _specificConfigId;
            set => SetProperty(ref _specificConfigId, value);
        }

        public bool AutomaticDNS
        {
            get => _automaticDNS;
            set => SetProperty(ref _automaticDNS, value);
        }

        public NetworkConfigurationPropertiesBase()
        {
            MacAddress = new byte[] { 0, 0, 0, 0, 0, 0 };
            InterfaceType = NetworkInterfaceType.Ethernet;
        }
    }
}
