//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using CommunityToolkit.Mvvm.ComponentModel;

namespace nanoFramework.Tools.Debugger
{
    public class X509DeviceCertificatesPropertiesBase : ObservableObject
    {
        private byte[] _certificate;
        private uint _certificateSize;

        public uint CertificateSize
        {
            get => _certificateSize;
            set => SetProperty(ref _certificateSize, value);
        }

        public byte[] Certificate
        {
            get => _certificate;
            set
            {
                if (SetProperty(ref _certificate, value))
                {
                    CertificateSize = (uint)value.Length;
                }
            }
        }
    }
}
