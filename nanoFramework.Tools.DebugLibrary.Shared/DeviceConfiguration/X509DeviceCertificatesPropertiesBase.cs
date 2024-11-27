//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using PropertyChanged;

namespace nanoFramework.Tools.Debugger
{
    [AddINotifyPropertyChangedInterface]
    public partial class X509DeviceCertificatesPropertiesBase
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
