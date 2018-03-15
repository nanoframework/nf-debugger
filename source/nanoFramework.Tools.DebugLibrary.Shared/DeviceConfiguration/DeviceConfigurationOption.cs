//
// Copyright (c) 2018 The nanoFramework project contributors
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
            ////////////////////////////////////////////////////////////////////////////////////
            // NEED TO KEEP THESE IN SYNC WITH native 'AddressMode' enum in nanoHAL_Network.h //
            ////////////////////////////////////////////////////////////////////////////////////

            /// <summary>
            /// Network configuration
            /// </summary>
            Network = 1,
        }
    }
}
