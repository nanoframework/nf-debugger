//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger
{
    public partial class DeviceConfiguration
    {
        /// <summary>
        ///  Device configuration option
        /// </summary>
        public enum DeviceConfigurationOption : byte
        {
            ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
            // NEED TO KEEP THESE IN SYNC WITH native 'DeviceConfigurationOption' enum in nanoHAL_ConfigurationManager.h //
            ///////////////////////////////////////////////////////////////////////////////////////////////////////////////

            /// <summary>
            /// Network configuration block
            /// </summary>
            Network = 1,

            /// <summary>
            /// Wireless Network configuration block
            /// </summary>
            Wireless80211Network = 2,

            /// <summary>
            /// Wireless Network as AP configuration block
            /// </summary>
            WirelessNetworkAP = 3,

            /// <summary>
            /// X509 Certificate block
            /// </summary>
            X509Certificate = 4,

            /// <summary>
            /// X509 Device Certificates
            /// </summary>
            X509DeviceCertificates = 5,

            /// <summary>
            /// All configuration blocks
            /// </summary>
            All = 255,
        }
    }
}
