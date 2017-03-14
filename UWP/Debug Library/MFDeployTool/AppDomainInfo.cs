//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using NanoFramework.Tools.Debugger.WireProtocol;

namespace NanoFramework.Tools.Debugger
{
    class AppDomainInfo : IAppDomainInfo
    {
        private uint m_id;
        Commands.Debugging_Resolve_AppDomain.Reply m_reply;

        public AppDomainInfo(uint id, Commands.Debugging_Resolve_AppDomain.Reply reply)
        {
            m_id = id;
            m_reply = reply;
        }

        public string Name
        {
            get { return m_reply.Name; }
        }

        public uint ID
        {
            get { return m_id; }
        }

        public uint[] AssemblyIndices
        {
            get { return m_reply.m_data; }
        }

    }
}
