﻿//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace nanoFramework.Tools.Debugger
{
    public class CLRCapabilities
    {
        /// <summary>
        /// Value of rotate right operation to get the platform capabilities as an integer.
        /// </summary>
        private static int s_platformCapabilitiesPosition = 16;

        /// <summary>
        /// Value of rotate right operation to get the target capabilities as an integer.
        /// </summary>
        private static int s_targetCapabilitiesPosition = 18;

        [Flags]
        public enum Capability : ulong
        {
            None =                      0x00000000,
            FloatingPoint =             0x00000001,
            SourceLevelDebugging =      0x00000002,
            AppDomains =                0x00000004,
            ExceptionFilters =          0x00000008,
            IncrementalDeployment =     0x00000010,
            SoftReboot =                0x00000020,
            Profiling =                 0x00000040,
            Profiling_Allocations =     0x00000080,
            Profiling_Calls =           0x00000100,
            ThreadCreateEx =            0x00000400,

            /// <summary>
            /// This flag indicates that the device requires em erase command before updating the configuration block.
            /// </summary>
            /// <remarks>
            /// *********************************************************************
            /// ** THIS FLAG IS DEPRECATED AND WILL BE REMOVED IN A FUTURE VERSION **
            /// ** USE Monitor_Ping_c_ConfigBlockRequiresErase INSTEAD             **
            /// *********************************************************************
            /// </remarks>
            [Obsolete("This flag is deprecated and it will be removed in a future version. Use Monitor_Ping_c_ConfigBlockRequiresErase instead.")]
            ConfigBlockRequiresErase =  0x00000800,

            /// <summary>
            /// This flag indicates that the device has nanoBooter.
            /// </summary>
            HasNanoBooter =             0x00001000,

            /// <summary>
            /// This flag indicates that the MAC address of the target can be changed.
            /// </summary>
            CanChangeMacAddress =       0x00002000,

            /// <summary>
            /// These bits are generic and are meant to be used to store platform specific capabilities.
            /// They should be parsed at the above layer to extract the real meaning out of them.
            /// </summary>
            PlatformCapability_0 =      0x01000000,
            PlatformCapability_1 =      0x02000000,
            PlatformCapability_2 =      0x04000000,
            PlatformCapability_3 =      0x08000000,

            PlatformCapability_Mask = PlatformCapability_0 | PlatformCapability_1 | PlatformCapability_2 | PlatformCapability_3,

            /// <summary>
            /// These bits are generic and are meant to be used to store target specific capabilities.
            /// They should be parsed at the above layer to extract the real meaning out of them.
            /// </summary>
            TargetCapability_0 =        0x10000000,
            TargetCapability_1 =        0x20000000,
            TargetCapability_2 =        0x40000000,
            TargetCapability_3 =        0x80000000,

            TargetCapability_Mask = TargetCapability_0 | TargetCapability_1 | TargetCapability_2 | TargetCapability_3,
        }

        public struct SoftwareVersionProperties
        {
            public readonly string BuildDate;
            public readonly string CompilerInfo;
            public readonly Version CompilerVersion;

            public SoftwareVersionProperties(byte[] buildDate, byte[] compilerInfo, uint compVersion)
            {
                // parse build date from byte[]
                char[] chars = new char[buildDate.Length];
                int i = 0;
                for (i = 0; i < chars.Length && buildDate[i] != 0; i++)
                {
                    chars[i] = (char)buildDate[i];
                }
                BuildDate = (new string(chars, 0, i)).TrimEnd('\0');

                // parse compiler info from byte[]
                chars = new char[compilerInfo.Length];
                i = 0;
                for (i = 0; i < chars.Length && compilerInfo[i] != 0; i++)
                {
                    chars[i] = (char)compilerInfo[i];
                }
                CompilerInfo = new string(chars, 0, i);

                // this is the compiler version in coded format: MAJOR x 10000 + MINOR x 100 + PATCH
                // example: v6.3.1 shows as 6 x 10000 + 3 x 100 + 1 = 60301
                // invalid version is -1
                try
                {
                    int major = (int)compVersion / 10000;
                    int minor = ((int)compVersion - (major * 10000)) / 100;
                    int patch = ((int)compVersion - (major * 10000) - (minor * 100));
                    CompilerVersion = new Version(major, minor, patch);
                }
                catch
                {
                    CompilerVersion = new Version(0, 0, 0);
                };
            }
        }

        public struct HalSystemInfoProperties
        {
            public readonly Version halVersion;
            public readonly string halVendorInfo;
            public readonly byte oemCode;
            public readonly byte modelCode;
            public readonly ushort skuCode;
            public readonly string moduleSerialNumber;
            public readonly string systemSerialNumber;

            public HalSystemInfoProperties(
                    Version hv, string hvi,
                    byte oc, byte mc, ushort sc,
                    byte[] mSerNumBytes, byte[] sSerNumBytes
                    )
            {
                halVersion = hv; halVendorInfo = hvi;
                oemCode = oc; modelCode = mc; skuCode = sc;

                moduleSerialNumber = BytesToHexString(mSerNumBytes).TrimEnd('\0');
                systemSerialNumber = BytesToHexString(sSerNumBytes).TrimEnd('\0');
            }

            private static string BytesToHexString(byte[] bytes)
            {
                System.Text.StringBuilder builder = new System.Text.StringBuilder();

                foreach (byte b in bytes)
                {
                    builder.Append(String.Format("{0:X}", b));
                }

                return builder.ToString();
            }
        }

        public struct ClrInfoProperties
        {
            public readonly Version clrVersion;
            public readonly string clrVendorInfo;
            public readonly Version targetFrameworkVersion;

            public ClrInfoProperties(Version cv, string cvi, Version tfv)
            {
                clrVersion = cv;
                clrVendorInfo = cvi;
                targetFrameworkVersion = tfv;
            }
        }

        public struct TargetInfoProperties
        {
            public readonly Version Version;
            public readonly string VendorInfo;
            public readonly string TargetName;
            public readonly string Platform;

            public TargetInfoProperties(Version version, string info, string target, string platform)
            {
                Version = version;
                VendorInfo = info.TrimEnd('\0');
                TargetName = target.TrimEnd('\0');
                Platform = platform.TrimEnd('\0');
            }
        }

        public struct NativeAssemblyProperties
        {
            public uint Checksum;
            public Version Version; // TODO add 'version info' in a future version
            public string Name;

            public NativeAssemblyProperties(string name, uint checksum, Version version)
            {
                Checksum = checksum;
                Name = name;
                Version = version;
            }
        }

        private readonly Capability m_capabilities;
        private SoftwareVersionProperties m_swVersion;
        private HalSystemInfoProperties m_halSystemInfo;
        private ClrInfoProperties m_clrInfo;
        private TargetInfoProperties m_targetReleaseInfo;
        private readonly List<NativeAssemblyProperties> m_nativeAssembliesInfo;

        private readonly bool m_fUnknown;

        public CLRCapabilities()
            : this(Capability.None, new SoftwareVersionProperties(),
                new HalSystemInfoProperties(), new ClrInfoProperties(), new TargetInfoProperties(), new List<NativeAssemblyProperties>())
        {
        }

        public CLRCapabilities(
            Capability capability,
            SoftwareVersionProperties ver,
            HalSystemInfoProperties halSystemInfo,
            ClrInfoProperties clrInfo,
            TargetInfoProperties solutionReleaseInfo,
            List<NativeAssemblyProperties> nativeAssembliesInfo
            )
        {
            m_fUnknown = (capability == Capability.None);
            m_capabilities = capability;
            m_swVersion = ver;

            m_halSystemInfo = halSystemInfo;
            m_clrInfo = clrInfo;
            m_targetReleaseInfo = solutionReleaseInfo;
            m_nativeAssembliesInfo = nativeAssembliesInfo;
        }

        public HalSystemInfoProperties HalSystemInfo
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return m_halSystemInfo;
            }
        }

        public ClrInfoProperties ClrInfo
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return m_clrInfo;
            }
        }

        public TargetInfoProperties SolutionReleaseInfo
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return m_targetReleaseInfo;
            }
        }

        public SoftwareVersionProperties SoftwareVersion
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return m_swVersion;
            }
        }
        public List<NativeAssemblyProperties> NativeAssemblies
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return m_nativeAssembliesInfo;
            }
        }

        public bool FloatingPoint
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return (m_capabilities & Capability.FloatingPoint) != 0;
            }
        }

        public bool SourceLevelDebugging
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return (m_capabilities & Capability.SourceLevelDebugging) != 0;
            }
        }

        public bool ThreadCreateEx
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return (m_capabilities & Capability.ThreadCreateEx) != 0;
            }
        }

        public bool AppDomains
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return (m_capabilities & Capability.AppDomains) != 0;
            }
        }

        public bool ExceptionFilters
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return (m_capabilities & Capability.ExceptionFilters) != 0;
            }
        }

        public bool IncrementalDeployment
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return (m_capabilities & Capability.IncrementalDeployment) != 0;
            }
        }

        public bool SoftReboot
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return (m_capabilities & Capability.SoftReboot) != 0;
            }
        }

        public bool Profiling
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return (m_capabilities & Capability.Profiling) != 0;
            }
        }

        public bool ProfilingAllocations
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return (m_capabilities & Capability.Profiling_Allocations) != 0;
            }
        }

        public bool ProfilingCalls
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return (m_capabilities & Capability.Profiling_Calls) != 0;
            }
        }

        public bool ConfigBlockRequiresErase
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return (m_capabilities & Capability.ConfigBlockRequiresErase) != 0;
            }
        }

        public bool HasNanoBooter
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return (m_capabilities & Capability.HasNanoBooter) != 0;
            }
        }

        public bool CanChangeMacAddress
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                return (m_capabilities & Capability.CanChangeMacAddress) != 0;
            }
        }

        public byte PlatformCapabilities
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                var platformCapabilites = (ulong)(m_capabilities & Capability.PlatformCapability_Mask);
                var value = platformCapabilites >> s_platformCapabilitiesPosition;
                return (byte)value;
            }
        }

        public byte TargetCapabilities
        {
            get
            {
                Debug.Assert(!m_fUnknown);
                var targetCapabilites = (ulong)(m_capabilities & Capability.TargetCapability_Mask);
                var value = targetCapabilites >> s_targetCapabilitiesPosition;
                return (byte)value;
            }
        }

        public bool IsUnknown
        {
            get { return m_fUnknown; }
        }
    }
}
