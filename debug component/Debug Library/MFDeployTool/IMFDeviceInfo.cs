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

namespace Microsoft.NetMicroFramework.Tools.MFDeployTool.Engine
{
    public interface IMFDeviceInfo
    {
        bool Valid { get; }
        System.Version HalBuildVersion { get; }
        string HalBuildInfo { get; }
        byte OEM { get; }
        byte Model { get; }
        ushort SKU { get; }
        string ModuleSerialNumber { get; }
        string SystemSerialNumber { get; }
        System.Version ClrBuildVersion { get; }
        string ClrBuildInfo { get; }
        System.Version TargetFrameworkVersion { get; }
        System.Version SolutionBuildVersion { get; }
        string SolutionBuildInfo { get; }
        IAppDomainInfo[] AppDomains { get; }
        IAssemblyInfo[] Assemblies { get; }
    }
}
