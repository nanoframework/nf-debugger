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

using Microsoft.SPOT.Debugger.WireProtocol;
using System.Collections.Generic;

namespace Microsoft.NetMicroFramework.Tools.MFDeployTool.Engine
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
