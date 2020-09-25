//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.Debugger
{
    class AppDomainInfo : IAppDomainInfo
    {
        private readonly uint m_id;
        readonly Commands.Debugging_Resolve_AppDomain.Reply m_reply;

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
