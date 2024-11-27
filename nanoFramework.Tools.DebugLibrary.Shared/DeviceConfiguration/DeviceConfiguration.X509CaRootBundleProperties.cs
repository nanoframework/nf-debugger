//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Text;

namespace nanoFramework.Tools.Debugger
{
    public partial class DeviceConfiguration
    {
        public partial class X509CaRootBundleProperties : X509CaRootBundlePropertiesBase
        {
            public bool IsUnknown { get; set; }

            public X509CaRootBundleProperties()
            {
            }

            public X509CaRootBundleProperties(X509CaRootBundleBase certificate)
            {
                CertificateSize = (uint)certificate.Certificate.Length;
                Certificate = certificate.Certificate;

                // reset unknown flag
                IsUnknown = false;
            }

            // operator to allow casting a X509CaRootBundleProperties object to X509CaRootBundleBase
            public static explicit operator X509CaRootBundleBase(X509CaRootBundleProperties value)
            {
                var x509Certificate = new X509CaRootBundleBase()
                {
                    Marker = Encoding.UTF8.GetBytes(MarkerConfigurationX509CaRootBundle_v1),

                    CertificateSize = (uint)value.Certificate.Length,
                    Certificate = value.Certificate,
                };

                return x509Certificate;
            }
        }
    }
}
