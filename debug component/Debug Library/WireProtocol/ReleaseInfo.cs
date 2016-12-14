//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the Microsoft .NET Micro Framework and is unsupported. 
// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use these files except in compliance with the License.
// You may obtain a copy of the License at:
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing
// permissions and limitations under the License.
// 
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using System;
using System.Text;

namespace Microsoft.SPOT.Debugger.WireProtocol
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
