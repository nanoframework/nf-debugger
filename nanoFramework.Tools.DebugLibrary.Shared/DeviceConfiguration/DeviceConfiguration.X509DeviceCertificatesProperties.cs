//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using CommunityToolkit.Mvvm.ComponentModel;
using System.Text;

namespace nanoFramework.Tools.Debugger
{
    public partial class DeviceConfiguration
    {
        public class X509DeviceCertificatesProperties : X509DeviceCertificatesPropertiesBase
        {
            private bool _isUnknown = true;

            public bool IsUnknown
            {
                get => _isUnknown;
                set => SetProperty(ref _isUnknown, value);
            }

            public X509DeviceCertificatesProperties()
            {
            }

            public X509DeviceCertificatesProperties(X509DeviceCertificatesBase certificate)
            {
                CertificateSize = (uint)certificate.Certificate.Length;
                Certificate = certificate.Certificate;

                // reset unknown flag
                IsUnknown = false;
            }

            // operator to allow casting a X509DeviceCertificatesProperties object to X509DeviceCertificatesBase
            public static explicit operator X509DeviceCertificatesBase(X509DeviceCertificatesProperties value)
            {
                var x509Certificate = new X509DeviceCertificatesBase()
                {
                    Marker = Encoding.UTF8.GetBytes(MarkerConfigurationX509DeviceCertificate_v1),

                    CertificateSize = (uint)value.Certificate.Length,
                    Certificate = value.Certificate,
                };

                return x509Certificate;
            }
        }
    }
}
