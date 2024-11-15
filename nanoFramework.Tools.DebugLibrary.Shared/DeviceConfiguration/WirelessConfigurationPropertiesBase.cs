//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using CommunityToolkit.Mvvm.ComponentModel;

namespace nanoFramework.Tools.Debugger
{
    /// <summary>
    /// Base class for common wireless configuration properties.
    /// </summary>
    public class WirelessConfigurationPropertiesBase : ObservableObject
    {
        private uint _id;
        private AuthenticationType _authentication;
        private EncryptionType _encryption;
        private RadioType _radio;
        private string _ssid;
        private string _password;
        private Wireless80211_ConfigurationOptions _options;

        /// <summary>
        /// Id of the configuration.
        /// </summary>
        public uint Id
        {
            get => _id;
            set => SetProperty(ref _id, value);
        }

        /// <summary>
        /// Authentication type for the network.
        /// </summary>
        public AuthenticationType Authentication
        {
            get => _authentication;
            set => SetProperty(ref _authentication, value);
        }

        /// <summary>
        /// Encryption type for the network.
        /// </summary>
        public EncryptionType Encryption
        {
            get => _encryption;
            set => SetProperty(ref _encryption, value);
        }

        /// <summary>
        /// Radio type for the network.
        /// </summary>
        public RadioType Radio
        {
            get => _radio;
            set => SetProperty(ref _radio, value);
        }

        /// <summary>
        /// SSID of the network.
        /// </summary>
        /// <remarks>Maximum allowed length for network password is 32.</remarks>
        public string Ssid
        {
            get => _ssid;
            set => SetProperty(ref _ssid, value);
        }

        /// <summary>
        /// Password for the network.
        /// </summary>
        /// <remarks>Maximum allowed length for network password is 64</remarks>
        public string Password
        {
            get => _password;
            set => SetProperty(ref _password, value);
        }

        /// <summary>
        /// Configuration options for the network.
        /// </summary>
        public Wireless80211_ConfigurationOptions Wireless80211Options
        {
            get => _options;
            set => SetProperty(ref _options, value);
        }
    }
}
