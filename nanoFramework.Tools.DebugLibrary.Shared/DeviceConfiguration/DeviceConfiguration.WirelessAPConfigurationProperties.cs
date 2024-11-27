//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Text;

namespace nanoFramework.Tools.Debugger
{
    public partial class DeviceConfiguration
    {
        public class WirelessAPConfigurationProperties : WirelessAPConfigurationPropertiesBase
        {
            public bool IsUnknown { get; set; }

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
                WirelessAPOptions = (WirelessAP_ConfigurationOptions)config.Options;
                Channel = config.Channel;
                MaxConnections = config.MaxConnections;

                // reset unknown flag
                IsUnknown = false;
            }

            // operator to allow casting a WirelessAPConfigurationProperties object to WirelessAPConfigurationBase
            public static explicit operator WirelessAPConfigurationBase(WirelessAPConfigurationProperties value)
            {
                var networkWirelessConfig = new WirelessAPConfigurationBase()
                {
                    Marker = Encoding.UTF8.GetBytes(MarkerConfigurationWireless80211AP_v1),

                    Id = value.Id,
                    Authentication = (byte)value.Authentication,
                    Encryption = (byte)value.Encryption,
                    Radio = (byte)value.Radio,
                    Options = (byte)value.WirelessAPOptions,
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
    }
}
