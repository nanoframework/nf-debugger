//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
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
                    var addressBytesReversed = address.GetAddressBytes().Reverse().ToArray();

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
