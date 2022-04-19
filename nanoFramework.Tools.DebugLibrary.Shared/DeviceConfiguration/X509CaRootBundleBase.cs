//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger
{
    public class X509CaRootBundleBase
    {
        /// <summary>
        /// This is the marker placeholder for this configuration block
        /// 4 bytes length.
        /// </summary>
        public byte[] Marker;

        /// <summary>
        /// Size of the certificate.
        /// </summary>
        public uint CertificateSize;

        /// <summary>
        /// Certificate
        /// </summary>
        public byte[] Certificate;

        public X509CaRootBundleBase()
        {
            // need to init these here to match the expected size on the struct to be sent to the device
            Marker = new byte[4];
            Certificate = new byte[64];
        }
    }
}