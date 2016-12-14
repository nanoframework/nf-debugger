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

using System.Collections.Generic;

namespace Microsoft.SPOT.Debugger.WireProtocol
{
    internal class TypeSysLookup
    {
        public enum Type : uint
        {
            Type,
            Method,
            Field
        }

        private Dictionary<ulong, object> m_lookup;

        private void EnsureHashtable()
        {
            lock (this)
            {
                if (m_lookup == null)
                {
                    m_lookup = new Dictionary<ulong, object>();
                }
            }
        }

        private ulong KeyFromTypeToken(Type type, uint token)
        {
            return ((ulong)type) << 32 | (ulong)token;
        }

        public object Lookup(Type type, uint token)
        {
            EnsureHashtable();

            ulong key = KeyFromTypeToken(type, token);

            return m_lookup[key];
        }

        public void Add(Type type, uint token, object val)
        {
            EnsureHashtable();

            ulong key = KeyFromTypeToken(type, token);

            m_lookup[key] = val;
        }
    }
}
