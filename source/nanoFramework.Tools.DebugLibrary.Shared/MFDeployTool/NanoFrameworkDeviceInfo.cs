//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.Text;

namespace nanoFramework.Tools.Debugger
{
    class NanoFrameworkDeviceInfo : INanoFrameworkDeviceInfo
    {
        private NanoDeviceBase m_self;

        private List<IAppDomainInfo> m_Domains = new List<IAppDomainInfo>();
        private List<IAssemblyInfo> m_AssemblyInfos = new List<IAssemblyInfo>();

        public NanoFrameworkDeviceInfo(NanoDeviceBase device)
        {
            m_self = device;

            Valid = false;
        }

        public bool GetDeviceInfo()
        {
            if (!Dbg.IsConnectedTonanoCLR) return false;

            // get app domains from device
            if (GetAppDomains())
            {
                // get assemblies from device
                if (GetAssemblies())
                {
                    Valid = true;

                    return true;
                }
            }

            return false;
        }

        private bool GetAppDomains()
        {
            if (Dbg.Capabilities.AppDomains)
            {
                Commands.Debugging_TypeSys_AppDomains.Reply domainsReply = Dbg.GetAppDomains();
                // TODO add cancellation token code

                if (domainsReply != null)
                {
                    foreach (uint id in domainsReply.Data)
                    {
                        Commands.Debugging_Resolve_AppDomain.Reply reply = Dbg.ResolveAppDomain(id);
                        // TODO add cancellation token code
                        if (reply != null)
                        {
                            m_Domains.Add(new AppDomainInfo(id, reply));
                        }
                    }

                    // sanity check
                    if (m_Domains.Count == domainsReply.Data.Length)
                    {
                        // we have all the domains listed
                        return true;
                    }
                }

                // default to failure
                return false;
            }
            else
            {
                // no app domains, so we are good here
                return true;
            }
        }

        private bool GetAssemblies()
        {
            // clear assembly list
            m_AssemblyInfos = new List<IAssemblyInfo>();

            List<Commands.DebuggingResolveAssembly> reply = Dbg.ResolveAllAssemblies();

            foreach (Commands.DebuggingResolveAssembly resolvedAssm in reply)
            {
                AssemblyInfoFromResolveAssembly ai = new AssemblyInfoFromResolveAssembly(resolvedAssm);

                foreach (IAppDomainInfo adi in m_Domains)
                {
                    if (Array.IndexOf<uint>(adi.AssemblyIndices, ai.Index) != -1)
                    {
                        ai.AddDomain(adi);
                    }
                }

                m_AssemblyInfos.Add(ai);
            }

            // sanity check
            if (m_AssemblyInfos.Count == reply.Count)
            {
                // we have all the assemblies listed
                return true;
            }

            // default to failure
            return false;
        }
        
        private Engine Dbg { get { return m_self.DebugEngine; } }

        public bool Valid { get; internal set; }

        public System.Version HalBuildVersion
        {
            get { return Dbg.Capabilities.HalSystemInfo.halVersion; }
        }

        public string HalBuildInfo
        {
            get { return Dbg.Capabilities.HalSystemInfo.halVendorInfo; }
        }

        public byte OEM
        {
            get { return Dbg.Capabilities.HalSystemInfo.oemCode; }
        }

        public byte Model
        {
            get { return Dbg.Capabilities.HalSystemInfo.modelCode; }
        }

        public ushort SKU
        {
            get { return Dbg.Capabilities.HalSystemInfo.skuCode; }
        }

        public string ModuleSerialNumber
        {
            get { return Dbg.Capabilities.HalSystemInfo.moduleSerialNumber; }
        }

        public string SystemSerialNumber
        {
            get { return Dbg.Capabilities.HalSystemInfo.systemSerialNumber; }
        }

        public System.Version ClrBuildVersion
        {
            get { return Dbg.Capabilities.ClrInfo.clrVersion; }
        }

        public string ClrBuildInfo
        {
            get { return Dbg.Capabilities.ClrInfo.clrVendorInfo; }
        }

        public System.Version TargetFrameworkVersion
        {
            get { return Dbg.Capabilities.ClrInfo.targetFrameworkVersion; }
        }

        public System.Version SolutionBuildVersion
        {
            get { return Dbg.Capabilities.SolutionReleaseInfo.Version; }
        }

        public string SolutionBuildInfo
        {
            get { return Dbg.Capabilities.SolutionReleaseInfo.VendorInfo; }
        }

        public string TargetName
        {
            get { return Dbg.Capabilities.SolutionReleaseInfo.TargetName; }
        }

        public string Platform
        {
            get { return Dbg.Capabilities.SolutionReleaseInfo.Platform; }
        }

        public byte PlatformCapabilities
        {
            get { return Dbg.Capabilities.PlatformCapabilities; }
        }

        public byte TargetCapabilities
        {
            get { return Dbg.Capabilities.TargetCapabilities; }
        }

        public IAppDomainInfo[] AppDomains
        {
            get { return m_Domains.ToArray(); }
        }

        public IAssemblyInfo[] Assemblies
        {
            get { return m_AssemblyInfos.ToArray(); }
        }

        public List<CLRCapabilities.NativeAssemblyProperties> NativeAssemblies => Dbg.Capabilities.NativeAssemblies;

        public string ImageBuildDate => Dbg.Capabilities.SoftwareVersion.BuildDate;

        public string ImageCompilerInfo => Dbg.Capabilities.SoftwareVersion.CompilerInfo;

        public Version ImageCompilerVersion => Dbg.Capabilities.SoftwareVersion.CompilerVersion;

        public override string ToString()
        {
            if (Valid)
            {
                try
                {
                    StringBuilder output = new StringBuilder();

                    output.AppendLine($"HAL build info: {HalBuildVersion?.ToString()}, {HalBuildInfo}");
                    output.AppendLine($"Target: {TargetName?.ToString()}");
                    output.AppendLine($"Platform: {Platform?.ToString()}");
                    output.AppendLine();
                    output.AppendLine($"Image build @ { ImageBuildDate ?? "unknown" } { ImageCompilerInfo ?? "unknown" } v{ ImageCompilerVersion?.ToString() }");
                    output.AppendLine();
                    output.AppendLine($"OEM Product codes (vendor, model, SKU): {OEM.ToString()}, {Model.ToString()}, {SKU.ToString()}");
                    output.AppendLine();
                    output.AppendLine("Serial Numbers (module, system):");
                    output.AppendLine("  " + ModuleSerialNumber);
                    output.AppendLine("  " + SystemSerialNumber);
                    output.AppendLine();
                    output.AppendLine($"Solution Build Info: {SolutionBuildVersion?.ToString()}, {SolutionBuildInfo}");

                    output.AppendLine();
                    output.AppendLine("AppDomains:");
                    foreach (IAppDomainInfo adi in AppDomains)
                    {
                        output.AppendLine($"  {adi.Name}, id={adi.ID}");
                    }

                    output.AppendLine();
                    output.AppendLine("Assemblies:");
                    foreach (IAssemblyInfo ai in Assemblies)
                    {
                        output.AppendLine($"  {ai.Name}, {ai.Version}");
                    }

                    output.AppendLine();
                    output.AppendLine("Native Assemblies:");
                    foreach (CLRCapabilities.NativeAssemblyProperties assembly in NativeAssemblies)
                    {
                        output.AppendLine($"  {assembly.Name} v{assembly.Version}, checksum 0x{assembly.Checksum.ToString("X8")}");
                    }

                    return output.ToString();
                }
                catch
                {
                    // catch everything, doesn't matter
                }
            }

            return "DeviceInfo is not valid!";
        }
    }
}
