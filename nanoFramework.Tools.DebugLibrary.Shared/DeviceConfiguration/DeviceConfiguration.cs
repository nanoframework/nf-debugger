//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using PropertyChanged;
using System;
using System.Collections.Generic;
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

        /// <summary>
        /// X509 CA Root bundle configuration marker
        /// </summary>
        public static string MarkerConfigurationX509CaRootBundle_v1 = "XB1\0";

        /// <summary>
        /// X509 Device certificate configuration marker
        /// </summary>
        public static string MarkerConfigurationX509DeviceCertificate_v1 = "XD1\0";

        /////////////////////////////////////////////////////////////

        /// <summary>
        /// Collection of <see cref="NetworkConfigurationProperties"/> blocks in a target device.
        /// </summary>
        public List<NetworkConfigurationProperties> NetworkConfigurations { get; set; }

        /// <summary>
        /// Collection of <see cref="Wireless80211ConfigurationProperties"/> blocks in a target device.
        /// </summary>
        public List<Wireless80211ConfigurationProperties> Wireless80211Configurations { get; set; }

        /// <summary>
        /// Collection of <see cref="WirelessAPConfigurationProperties"/> blocks in a target device.
        /// </summary>
        public List<WirelessAPConfigurationProperties> WirelessAPConfigurations { get; set; }

        /// <summary>
        /// Collection of <see cref="X509CaRootBundleProperties"/> blocks in a target device.
        /// </summary>
        public List<X509CaRootBundleProperties> X509Certificates { get; set; }

        /// <summary>
        /// Collection of <see cref="X509DeviceCertificatesProperties"/> blocks in a target device.
        /// </summary>
        public List<X509DeviceCertificatesProperties> X509DeviceCertificates { get; }

        public DeviceConfiguration()
            : this(new List<NetworkConfigurationProperties>(),
                   new List<Wireless80211ConfigurationProperties>(),
                   new List<WirelessAPConfigurationProperties>(),
                   new List<X509CaRootBundleProperties>(),
                   new List<X509DeviceCertificatesProperties>())
        {
        }

        public DeviceConfiguration(
            List<NetworkConfigurationProperties> networkConfiguratons,
            List<Wireless80211ConfigurationProperties> networkWirelessConfiguratons,
            List<WirelessAPConfigurationProperties> networkWirelessAPConfiguratons,
            List<X509CaRootBundleProperties> x509Certificates,
            List<X509DeviceCertificatesProperties> x509DeviceCertificates
            )
        {
            NetworkConfigurations = networkConfiguratons;
            Wireless80211Configurations = networkWirelessConfiguratons;
            WirelessAPConfigurations = networkWirelessAPConfiguratons;
            X509Certificates = x509Certificates;
            X509DeviceCertificates = x509DeviceCertificates;
        }

        // operator to allow cast_ing a DeviceConfiguration object to DeviceConfigurationBase
        public static explicit operator DeviceConfigurationBase(DeviceConfiguration value)
        {
            return new DeviceConfigurationBase()
            {
                NetworkConfigurations = value.NetworkConfigurations.Select(i => (NetworkConfigurationBase)i).ToArray(),
                Wireless80211Configurations = value.Wireless80211Configurations.Select(i => (Wireless80211ConfigurationBase)i).ToArray(),
                WirelessAPConfigurations = value.WirelessAPConfigurations.Select(i => (WirelessAPConfigurationBase)i).ToArray(),
                X509CaRootBundle = value.X509Certificates.Select(i => (X509CaRootBundleBase)i).ToArray(),
                X509DeviceCertificates = value.X509DeviceCertificates.Select(i => (X509DeviceCertificatesBase)i).ToArray()
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
                var addressBytesReversed = address.GetAddressBytes().Reverse().ToArray();

                return new uint[] { BitConverter.ToUInt32(addressBytesReversed, 0),
                                    BitConverter.ToUInt32(addressBytesReversed, 4),
                                    BitConverter.ToUInt32(addressBytesReversed, 8),
                                    BitConverter.ToUInt32(addressBytesReversed, 12) };
            }
            catch { };

            return new uint[4];
        }

        /////////////////////////////////////////////////////////////

        [AddINotifyPropertyChangedInterface]
        public class NetworkConfigurationProperties : NetworkConfigurationPropertiesBase
        {
            internal const uint EmptySpecificConfigValue = uint.MaxValue;

            public bool IsUnknown { get; set; } = true;

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
                    StartupAddressMode = (byte)value.StartupAddressMode,
                };

                networkConfig.AutomaticDNS = value.AutomaticDNS ? (byte)1 : (byte)0;
                networkConfig.SpecificConfigId = value.SpecificConfigId ?? EmptySpecificConfigValue;

                return networkConfig;
            }

        }

        [AddINotifyPropertyChangedInterface]
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
                Ssid = Encoding.UTF8.GetString(config.Ssid).Trim('\0');
                Password = Encoding.UTF8.GetString(config.Password).Trim('\0');
                Options = (Wireless80211_ConfigurationOptions)config.Options;

                // reset unknown flag
                IsUnknown = false;
            }

            // operator to allow cast_ing a Wireless80211ConfigurationProperties object to Wireless80211ConfigurationBase
            public static explicit operator Wireless80211ConfigurationBase(Wireless80211ConfigurationProperties value)
            {
                var networkWirelessConfig = new Wireless80211ConfigurationBase()
                {
                    Marker = Encoding.UTF8.GetBytes(MarkerConfigurationWireless80211_v1),

                    Id = value.Id,
                    Authentication = (byte)value.Authentication,
                    Encryption = (byte)value.Encryption,
                    Radio = (byte)value.Radio,
                    Options = (byte)value.Options
                };

                // the following ones are strings so they need to be copied over to the array 
                // this is required to when serializing the class the struct size matches the one in the native end
                Array.Copy(Encoding.UTF8.GetBytes(value.Ssid), 0, networkWirelessConfig.Ssid, 0, value.Ssid.Length);
                Array.Copy(Encoding.UTF8.GetBytes(value.Password), 0, networkWirelessConfig.Password, 0, value.Password.Length);

                return networkWirelessConfig;
            }

        }

        [AddINotifyPropertyChangedInterface]
        public class WirelessAPConfigurationProperties : WirelessAPConfigurationPropertiesBase
        {
            public bool IsUnknown { get; set; } = true;

            public WirelessAPConfigurationProperties()
            {

            }

            public WirelessAPConfigurationProperties(WirelessAPConfigurationBase config)
            {
                Id = config.Id;
                Authentication = (AuthenticationType)config.Authentication;
                Encryption = (EncryptionType)config.Encryption;
                Radio = (RadioType)config.Radio;
                Ssid = Encoding.UTF8.GetString(config.Ssid).Trim('\0');
                Password = Encoding.UTF8.GetString(config.Password).Trim('\0');
                Options = (WirelessAP_ConfigurationOptions)config.Options;

                // reset unknown flag
                IsUnknown = false;
            }

            // operator to allow cast_ing a WirelessAPConfigurationProperties object to WirelessAPConfigurationBase
            public static explicit operator WirelessAPConfigurationBase(WirelessAPConfigurationProperties value)
            {
                var networkWirelessConfig = new WirelessAPConfigurationBase()
                {
                    Marker = Encoding.UTF8.GetBytes(MarkerConfigurationWireless80211AP_v1),

                    Id = value.Id,
                    Authentication = (byte)value.Authentication,
                    Encryption = (byte)value.Encryption,
                    Radio = (byte)value.Radio,
                    Options = (byte)value.Options,
                    Channel = value.Channel,
                    MaxConnections = value.MaxConnections
                };

                // the following ones are strings so they need to be copied over to the array 
                // this is required to when serializing the class the struct size matches the one in the native end
                Array.Copy(Encoding.UTF8.GetBytes(value.Ssid), 0, networkWirelessConfig.Ssid, 0, value.Ssid.Length);
                Array.Copy(Encoding.UTF8.GetBytes(value.Password), 0, networkWirelessConfig.Password, 0, value.Password.Length);

                return networkWirelessConfig;
            }

        }

        [AddINotifyPropertyChangedInterface]
        public class X509CaRootBundleProperties : X509CaRootBundlePropertiesBase
        {
            public bool IsUnknown { get; set; } = true;

            public X509CaRootBundleProperties()
            {

            }

            public X509CaRootBundleProperties(X509CaRootBundleBase certificate)
            {
                CertificateSize = (uint)certificate.Certificate.Length;
                Certificate = certificate.Certificate;

                // reset unknown flag
                IsUnknown = false;
            }

            // operator to allow cast_ing a X509CaRootBundleBaseProperties object to X509CaRootBundleBase
            public static explicit operator X509CaRootBundleBase(X509CaRootBundleProperties value)
            {
                var x509Certificate = new X509CaRootBundleBase()
                {
                    Marker = Encoding.UTF8.GetBytes(MarkerConfigurationX509CaRootBundle_v1),

                    CertificateSize = (uint)value.Certificate.Length,
                    Certificate = value.Certificate,
                };

                return x509Certificate;
            }
        }

        [AddINotifyPropertyChangedInterface]
        public class X509DeviceCertificatesProperties : X509DeviceCertificatesPropertiesBase
        {
            public bool IsUnknown { get; set; } = true;

            public X509DeviceCertificatesProperties()
            {

            }

            public X509DeviceCertificatesProperties(X509DeviceCertificatesBase certificate)
            {
                CertificateSize = (uint)certificate.Certificate.Length;
                Certificate = certificate.Certificate;

                // reset unknown flag
                IsUnknown = false;
            }

            // operator to allow cast_ing a X509DeviceCertificatesBaseProperties object to X509DeviceCertificatesBase
            public static explicit operator X509DeviceCertificatesBase(X509DeviceCertificatesProperties value)
            {
                var x509Certificate = new X509DeviceCertificatesBase()
                {
                    Marker = Encoding.UTF8.GetBytes(MarkerConfigurationX509DeviceCertificate_v1),

                    CertificateSize = (uint)value.Certificate.Length,
                    Certificate = value.Certificate,
                };

                return x509Certificate;
            }
        }

        /////////////////////////////////////////////////////////////

    }
}
