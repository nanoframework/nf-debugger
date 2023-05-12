//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using PropertyChanged;

namespace nanoFramework.Tools.Debugger
{
    /// <summary>
    /// Base class for wireless Access Poing configuration properties.
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class WirelessAPConfigurationPropertiesBase
    {
        /// <summary>
        /// Id of the configuration.
        /// </summary>
        public uint Id { get; set; }

        /// <summary>
        /// Authentication type for the network.
        /// </summary>
        public AuthenticationType Authentication { get; set; }

        /// <summary>
        /// Encryption type for the network.
        /// </summary>
        public EncryptionType Encryption { get; set; }

        /// <summary>
        /// Radio type for the network.
        /// </summary>
        public RadioType Radio { get; set; }

        /// <summary>
        /// SSID of the network.
        /// </summary>
        /// <remarks>Maximum allowed length for network password is 32.</remarks>
        public string Ssid { get; set; }

        /// <summary>
        /// Password for the network.
        /// </summary>
        /// <remarks>Maximum allowed length for network password is 64</remarks>
        public string Password { get; set; }

        /// <summary>
        /// Configuration options for the network.
        /// </summary>
        public WirelessAP_ConfigurationOptions Options { get; set; }

        /// <summary>
        /// Channel for the network.
        /// </summary>
        public byte Channel { get; set; }

        /// <summary>
        /// Maximum number of connections allowed.
        /// </summary>
        public byte MaxConnections { get; set; }
    }
}