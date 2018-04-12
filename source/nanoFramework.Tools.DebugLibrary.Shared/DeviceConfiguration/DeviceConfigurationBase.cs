//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//


namespace nanoFramework.Tools.Debugger
{
    public class DeviceConfigurationBase
    {
        public NetworkConfigurationBase[] NetworkConfigurations;

        public NetworkWirelessConfigurationBase[] NetworkWirelessConfigurations { get; internal set; }
    }
}
