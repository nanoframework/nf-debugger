//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Text;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class ReleaseInfo : IConverter
    {
        public VersionStruct m_version;
        public byte[] m_info;

        public ReleaseInfo()
        {
            m_info = new byte[64 - 8];
        }

        public void PrepareForDeserialize(int size, byte[] data, Converter converter)
        {
            m_info = new byte[64 - 8];
        }

        public Version Version
        {
            get { return m_version.Version; }
        }

        public string Info
        {
            get
            {
                return Encoding.UTF8.GetString(m_info, 0, m_info.Length);
            }
        }
    }
}
