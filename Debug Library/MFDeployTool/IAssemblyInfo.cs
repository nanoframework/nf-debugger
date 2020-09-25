//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System.Collections.Generic;

namespace nanoFramework.Tools.Debugger
{
    public interface IAssemblyInfo
    {
        string Name { get; }
        System.Version Version { get; }
        uint Index { get; }
        List<IAppDomainInfo> InAppDomains { get; }
    }
}
