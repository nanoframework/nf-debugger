//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using System.Collections.Generic;

namespace nanoFramework.Tools.Debugger
{
    class AssemblyInfoFromResolveAssembly : IAssemblyInfo
    {
        private readonly Commands.DebuggingResolveAssembly _dra;
        private readonly List<IAppDomainInfo> _appDomains = new List<IAppDomainInfo>();

        public AssemblyInfoFromResolveAssembly(Commands.DebuggingResolveAssembly dra)
        {
            _dra = dra;
        }

        public string Name
        {
            get { return _dra.Result.Name; }
        }

        public System.Version Version
        {
            get
            {
                Commands.DebuggingResolveAssembly.Version version = _dra.Result.Version;
                return new System.Version(version.MajorVersion, version.MinorVersion, version.BuildNumber, version.RevisionNumber);
            }
        }

        public uint Index
        {
            get { return _dra.Idx; }
        }

        public List<IAppDomainInfo> InAppDomains
        {
            get { return _appDomains; }
        }

        public void AddDomain(IAppDomainInfo adi)
        {
            if (adi != null)
            {
                _appDomains.Add(adi);
            }
        }
    }
}
