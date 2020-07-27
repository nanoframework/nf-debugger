//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//


namespace nanoFramework.Tools.Debugger
{
    public class DeviceConfigurationBase
    {
        public NetworkConfigurationBase[] NetworkConfigurations;

        public Wireless80211ConfigurationBase[] Wireless80211Configurations { get; internal set; }

        public WirelessAPConfigurationBase[] WirelessAPConfigurations { get; internal set; }

        public X509CaRootBundleBase[] X509CaRootBundle { get; internal set; }
    }
}
