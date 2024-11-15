//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger
{
    /// <summary>
    /// Base class for wireless Access Point configuration properties.
    /// </summary>
    public class WirelessAPConfigurationPropertiesBase : WirelessConfigurationPropertiesBase
    {
        private byte _channel;
        private byte _maxConnections;

        /// <summary>
        /// Channel for the network.
        /// </summary>
        public byte Channel
        {
            get => _channel;
            set => SetProperty(ref _channel, value);
        }

        /// <summary>
        /// Maximum number of connections allowed.
        /// </summary>
        public byte MaxConnections
        {
            get => _maxConnections;
            set => SetProperty(ref _maxConnections, value);
        }

        /// <summary>
        /// Configuration options for the network.
        /// </summary>
        public WirelessAP_ConfigurationOptions WirelessAPOptions { get; set; }
    }
}
