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

namespace Microsoft.NetMicroFramework.Tools.MFDeployTool.Engine
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
