﻿//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System.Collections.Generic;

namespace nanoFramework.Tools.Debugger.WireProtocol
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
            return ((ulong)type) << 32 | token;
        }

        public object Lookup(Type type, uint token)
        {
            EnsureHashtable();

            ulong key = KeyFromTypeToken(type, token);

            // need to use a try method because the key may not exist
            m_lookup.TryGetValue(key, out object typeValue);

            return typeValue;
        }

        public void Add(Type type, uint token, object val)
        {
            EnsureHashtable();

            ulong key = KeyFromTypeToken(type, token);

            m_lookup[key] = val;
        }
    }
}
