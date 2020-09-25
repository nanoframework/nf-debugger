//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using System.Collections.Generic;

namespace nanoFramework.Tools.Debugger
{
    class AssemblyInfoFromResolveAssembly : IAssemblyInfo
    {
        private Commands.Debugging_Resolve_Assembly m_dra;
        private List<IAppDomainInfo> m_AppDomains = new List<IAppDomainInfo>();

        public AssemblyInfoFromResolveAssembly(Commands.Debugging_Resolve_Assembly dra)
        {
            m_dra = dra;
        }

        public string Name
        {
            get { return m_dra.m_reply.Name; }
        }

        public System.Version Version
        {
            get
            {
                Commands.Debugging_Resolve_Assembly.Version draver = m_dra.m_reply.m_version;
                return new System.Version(draver.iMajorVersion, draver.iMinorVersion, draver.iBuildNumber, draver.iRevisionNumber);
            }
        }

        public uint Index
        {
            get { return m_dra.m_idx; }
        }

        public List<IAppDomainInfo> InAppDomains
        {
            get { return m_AppDomains; }
        }

        public void AddDomain(IAppDomainInfo adi)
        {
            if (adi != null)
            {
                m_AppDomains.Add(adi);
            }
        }
    }
}
