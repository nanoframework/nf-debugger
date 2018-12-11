//
// Copyright (c) 2018 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using PropertyChanged;

namespace nanoFramework.Tools.Debugger
{
    [AddINotifyPropertyChangedInterface]
    public class X509CaRootBundlePropertiesBase
    {
        private byte[] _certificate;

        public uint CertificateSize { get; set; }

        public byte[] Certificate
        {
            get => _certificate;
            set
            {
                _certificate = value;
                CertificateSize = (uint)value.Length;
            }
        }
    }
}