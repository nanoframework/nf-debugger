//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System.Collections.Generic;

namespace NanoFramework.Tools.Debugger.WireProtocol
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
