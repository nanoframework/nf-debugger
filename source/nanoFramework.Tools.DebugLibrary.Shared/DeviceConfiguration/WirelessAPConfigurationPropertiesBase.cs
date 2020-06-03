﻿//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using PropertyChanged;
using System.ComponentModel.DataAnnotations;

namespace nanoFramework.Tools.Debugger
{
    [AddINotifyPropertyChangedInterface]
    public class WirelessAPConfigurationPropertiesBase
    {
        public uint Id { get; set; }
        public AuthenticationType Authentication { get; set; }
        public EncryptionType Encryption { get; set; }
        public RadioType Radio { get; set; }
        [MaxLength(32, ErrorMessage = "Maximum allowed length for SSID is 32.")]
        public string Ssid { get; set; }
        [MaxLength(64, ErrorMessage = "Maximum allowed length for network password is 64.")]
        public string Password { get; set; }
        public WirelessAP_ConfigurationOptions Options { get; set; }
        public byte Channel { get; set; }
        public byte MaxConnections { get; set; }
    }
}