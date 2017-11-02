//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

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

        public async Task<bool> GetDeviceInfo()
        {
            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            if (!Dbg.IsConnectedTonanoCLR) return false;

            // get app domains from device
            if (await GetAppDomainsAsync(cancelTSource.Token).ConfigureAwait(true))
            {
                // get assemblies from device
                if (await GetAssembliesAsync(cancelTSource.Token).ConfigureAwait(true))
                {

                    Valid = true;

                    return true;
                }
            }

            return false;
        }

        private async Task<bool> GetAppDomainsAsync(CancellationToken cancellationToken)
        {
            if (Dbg.Capabilities.AppDomains)
            {
                Commands.Debugging_TypeSys_AppDomains.Reply domainsReply = await Dbg.GetAppDomainsAsync().ConfigureAwait(true);
                // TODO add cancellation token code

                if (domainsReply != null)
                {
                    foreach (uint id in domainsReply.Data)
                    {
                        Commands.Debugging_Resolve_AppDomain.Reply reply = await Dbg.ResolveAppDomainAsync(id).ConfigureAwait(true);
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

        private async Task<bool> GetAssembliesAsync(CancellationToken cancellationToken)
        {
            List<Commands.DebuggingResolveAssembly> reply = await Dbg.ResolveAllAssembliesAsync(cancellationToken).ConfigureAwait(true);

            if (reply != null)
            {
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
            get { return Dbg.Capabilities.SolutionReleaseInfo.targetVersion; }
        }

        public string SolutionBuildInfo
        {
            get { return Dbg.Capabilities.SolutionReleaseInfo.targetVendorInfo; }
        }

        public IAppDomainInfo[] AppDomains
        {
            get { return m_Domains.ToArray(); }
        }

        public IAssemblyInfo[] Assemblies
        {
            get { return m_AssemblyInfos.ToArray(); }
        }

        public string ImageBuildDate => Dbg.Capabilities.SoftwareVersion.BuildDate;

        public Version ImageCompilerVersion => Dbg.Capabilities.SoftwareVersion.CompilerVersion;

        public override string ToString()
        {
            if (Valid)
            {
                try
                {
                    StringBuilder output = new StringBuilder();

                    output.AppendLine(String.Format("HAL build info: {0}, {1}", HalBuildVersion?.ToString(), HalBuildInfo?.TrimEnd('\0')));
                    output.AppendLine(String.Format($"Image build @ {ImageBuildDate.TrimEnd('\0')} GNU ARM GCC v{ ImageCompilerVersion.ToString() }"));
                    output.AppendLine(String.Format("OEM Product codes (vendor, model, SKU): {0}, {1}, {2}", OEM.ToString(), Model.ToString(), SKU.ToString()));
                    output.AppendLine("Serial Numbers (module, system):");
                    output.AppendLine("  " + ModuleSerialNumber?.TrimEnd('\0'));
                    output.AppendLine("  " + SystemSerialNumber?.TrimEnd('\0'));
                    output.AppendLine(String.Format("Solution Build Info: {0}, {1}", SolutionBuildVersion?.ToString(), SolutionBuildInfo?.TrimEnd('\0')));

                    output.AppendLine("AppDomains:");
                    foreach (IAppDomainInfo adi in AppDomains)
                    {
                        output.AppendLine(String.Format("  {0}, id={1}", adi.Name, adi.ID));
                    }

                    output.AppendLine("Assemblies:");
                    foreach (IAssemblyInfo ai in Assemblies)
                    {
                        output.AppendLine(String.Format("  {0}, {1}", ai.Name, ai.Version));
                    }

                    return output.ToString();
                }
                catch { };
            }

            return "DeviceInfo is not valid!";
        }
    }
}
