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
        public class Wireless80211ConfigurationProperties : Wireless80211ConfigurationPropertiesBase
        {
            public bool IsUnknown { get; set; }

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
                Wireless80211Options = (Wireless80211_ConfigurationOptions)config.Options;

                // reset unknown flag
                IsUnknown = false;
            }

            // operator to allow casting a Wireless80211ConfigurationProperties object to Wireless80211ConfigurationBase
            public static explicit operator Wireless80211ConfigurationBase(Wireless80211ConfigurationProperties value)
            {
                var networkWirelessConfig = new Wireless80211ConfigurationBase()
                {
                    Marker = Encoding.UTF8.GetBytes(MarkerConfigurationWireless80211_v1),

                    Id = value.Id,
                    Authentication = (byte)value.Authentication,
                    Encryption = (byte)value.Encryption,
                    Radio = (byte)value.Radio,
                    Options = (byte)value.Wireless80211Options
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
