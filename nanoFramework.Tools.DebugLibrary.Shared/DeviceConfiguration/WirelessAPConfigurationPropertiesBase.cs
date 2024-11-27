//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger
{
    /// <summary>
    /// Base class for wireless Access Point configuration properties.
    /// </summary>
    public partial class WirelessAPConfigurationPropertiesBase : WirelessConfigurationPropertiesBase
    {
        /// <summary>
        /// Channel for the network.
        /// </summary>
        public byte Channel { get; set; }

        /// <summary>
        /// Maximum number of connections allowed.
        /// </summary>
        public byte MaxConnections { get; set; }

        /// <summary>
        /// Configuration options for the network.
        /// </summary>
        public WirelessAP_ConfigurationOptions WirelessAPOptions { get; set; }
    }
}
