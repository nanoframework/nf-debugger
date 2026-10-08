//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;

namespace nanoFramework.Tools.Debugger.Compatibility
{
    /// <summary>
    /// Checks that a set of nanoFramework assemblies (the whole deployment) can run on a firmware.
    /// </summary>
    /// <remarks>
    /// Three checks are performed:
    /// <list type="bullet">
    /// <item><description>format: all the PEs must have the format expected by the firmware (when known) and the deployment can't mix PE formats.</description></item>
    /// <item><description>native: each assembly requiring a native counterpart (native hash != 0) must have one in the firmware, with the same native contract hash.</description></item>
    /// <item><description>managed: each assembly reference must be satisfied by an assembly in the deployment with the same name and the same major.minor version (same rule as nanoCLR CLR_RT_TypeSystem::FindAssembly with fExact = false).</description></item>
    /// </list>
    /// Assemblies with a format issue are not checked for native and reference issues, as they have to be rebuilt anyway.
    /// They still satisfy references from other assemblies, to avoid reporting the same problem twice.
    /// </remarks>
    public static class DeploymentCompatibility
    {
        /// <summary>
        /// First major version of nanoCLR that loads <see cref="PeFormat.V2"/> PEs.
        /// </summary>
        public const int FirstNanoClrMajorVersionWithPeFormatV2 = 2;

        /// <summary>
        /// Gets the PE format loaded by a firmware, from its nanoCLR version.
        /// </summary>
        /// <param name="nanoClrVersion">
        /// Version of nanoCLR reported by the device (<see cref="CLRCapabilities.ClrInfoProperties.clrVersion"/>). This is the nanoCLR version, not the nanoBooter one.
        /// </param>
        /// <returns>
        /// <see cref="PeFormat.V2"/> for nanoCLR 2.x and later, <see cref="PeFormat.V1"/> for nanoCLR 1.x.
        /// <see langword="null"/> when the version is unknown (not reported, or 0.x as reported by local development builds):
        /// in that case no PE format is enforced and only a deployment mixing PE formats is reported.
        /// </returns>
        public static PeFormat? GetDevicePeFormat(Version nanoClrVersion)
        {
            if (nanoClrVersion is null || nanoClrVersion.Major < 1)
            {
                return null;
            }

            return nanoClrVersion.Major >= FirstNanoClrMajorVersionWithPeFormatV2 ? PeFormat.V2 : PeFormat.V1;
        }

        /// <summary>
        /// Gets the PE format loaded by the firmware of the device connected to a debug engine, from the nanoCLR version in its capabilities.
        /// </summary>
        /// <param name="engine">Debug engine connected to nanoCLR (capabilities already queried).</param>
        /// <returns>The PE format, or <see langword="null"/> if unknown (see <see cref="GetDevicePeFormat(Version)"/>).</returns>
        public static PeFormat? GetDevicePeFormat(Engine engine)
        {
            CLRCapabilities capabilities = engine?.Capabilities;

            if (capabilities is null || capabilities.IsUnknown)
            {
                // e.g. connected to nanoBooter: the nanoCLR version is not known
                return null;
            }

            return GetDevicePeFormat(capabilities.ClrInfo.clrVersion);
        }

        /// <summary>
        /// Checks PE files and/or deployment images against a connected device: its native assemblies and the PE format its firmware loads.
        /// </summary>
        /// <param name="peOrImagePaths">Paths to the PE files and/or deployment images making the whole deployment.</param>
        /// <param name="device">Connected device (device information and debug engine already populated).</param>
        /// <exception cref="System.IO.InvalidDataException">One of the files is not a valid PE file or deployment image.</exception>
        public static CompatibilityCheckResult Check(
            IEnumerable<string> peOrImagePaths,
            NanoDeviceBase device)
        {
            if (device is null)
            {
                throw new ArgumentNullException(nameof(device));
            }

            return Check(
                peOrImagePaths,
                device.DeviceInfo.NativeAssemblies,
                GetDevicePeFormat(device.DebugEngine));
        }

        /// <summary>
        /// Checks PE files and/or deployment images against the device connected to a debug engine: its native assemblies and the PE format its firmware loads.
        /// </summary>
        /// <param name="peOrImagePaths">Paths to the PE files and/or deployment images making the whole deployment.</param>
        /// <param name="engine">Debug engine connected to the device (capabilities already queried).</param>
        /// <exception cref="System.IO.InvalidDataException">One of the files is not a valid PE file or deployment image.</exception>
        public static CompatibilityCheckResult Check(
            IEnumerable<string> peOrImagePaths,
            Engine engine)
        {
            if (engine is null)
            {
                throw new ArgumentNullException(nameof(engine));
            }

            return Check(
                peOrImagePaths,
                engine.Capabilities.NativeAssemblies,
                GetDevicePeFormat(engine));
        }

        /// <summary>
        /// Checks PE files and/or deployment images against the native assemblies reported by a device.
        /// </summary>
        /// <param name="peOrImagePaths">Paths to the PE files and/or deployment images making the whole deployment.</param>
        /// <param name="deviceNativeAssemblies">Native assemblies reported by the device (<see cref="CLRCapabilities.NativeAssemblies"/>).</param>
        /// <param name="expectedFormat">
        /// PE format loaded by the device firmware (see <see cref="GetDevicePeFormat(Version)"/>).
        /// When <see langword="null"/>, only a deployment mixing PE formats is reported.
        /// </param>
        /// <exception cref="System.IO.InvalidDataException">One of the files is not a valid PE file or deployment image.</exception>
        public static CompatibilityCheckResult Check(
            IEnumerable<string> peOrImagePaths,
            IEnumerable<CLRCapabilities.NativeAssemblyProperties> deviceNativeAssemblies,
            PeFormat? expectedFormat) =>
            Check(peOrImagePaths, NativeAssemblyDescriptor.FromDevice(deviceNativeAssemblies), expectedFormat);

        /// <summary>
        /// Checks PE files and/or deployment images against the native assemblies listed in a firmware package manifest.
        /// </summary>
        /// <param name="peOrImagePaths">Paths to the PE files and/or deployment images making the whole deployment.</param>
        /// <param name="manifest">Manifest of the firmware package (native_assemblies.json).</param>
        /// <exception cref="System.IO.InvalidDataException">One of the files is not a valid PE file or deployment image.</exception>
        public static CompatibilityCheckResult Check(
            IEnumerable<string> peOrImagePaths,
            NativeAssembliesManifest manifest)
        {
            if (manifest is null)
            {
                throw new ArgumentNullException(nameof(manifest));
            }

            return Check(peOrImagePaths, manifest.NativeAssemblies, manifest.ExpectedPeFormat);
        }

        /// <summary>
        /// Checks PE files and/or deployment images against a list of native assemblies.
        /// </summary>
        /// <param name="peOrImagePaths">Paths to the PE files and/or deployment images making the whole deployment.</param>
        /// <param name="nativeAssemblies">Native assemblies available in the firmware.</param>
        /// <param name="expectedFormat">
        /// PE format required by the firmware, if known. When <see langword="null"/>, only a deployment mixing PE formats is reported.
        /// </param>
        /// <exception cref="System.IO.InvalidDataException">One of the files is not a valid PE file or deployment image.</exception>
        public static CompatibilityCheckResult Check(
            IEnumerable<string> peOrImagePaths,
            IEnumerable<NativeAssemblyDescriptor> nativeAssemblies,
            PeFormat? expectedFormat = null)
        {
            if (peOrImagePaths is null)
            {
                throw new ArgumentNullException(nameof(peOrImagePaths));
            }

            return Check(peOrImagePaths.SelectMany(PeFileReader.ReadFile).ToList(), nativeAssemblies, expectedFormat);
        }

        /// <summary>
        /// Checks assemblies against a list of native assemblies.
        /// </summary>
        /// <param name="assemblies">Assemblies making the whole deployment.</param>
        /// <param name="nativeAssemblies">Native assemblies available in the firmware.</param>
        /// <param name="expectedFormat">
        /// PE format required by the firmware, if known. When <see langword="null"/>, only a deployment mixing PE formats is reported.
        /// </param>
        public static CompatibilityCheckResult Check(
            IEnumerable<PeAssemblyInfo> assemblies,
            IEnumerable<NativeAssemblyDescriptor> nativeAssemblies,
            PeFormat? expectedFormat = null)
        {
            if (assemblies is null)
            {
                throw new ArgumentNullException(nameof(assemblies));
            }

            if (nativeAssemblies is null)
            {
                throw new ArgumentNullException(nameof(nativeAssemblies));
            }

            List<PeAssemblyInfo> assemblyList = assemblies.ToList();
            var issues = new List<CompatibilityIssue>();

            HashSet<PeAssemblyInfo> wrongFormat = CheckFormat(assemblyList, expectedFormat, issues);
            List<PeAssemblyInfo> toCheck = assemblyList.Where(a => !wrongFormat.Contains(a)).ToList();

            CheckNative(toCheck, nativeAssemblies, issues);
            CheckReferences(toCheck, assemblyList, issues);

            return new CompatibilityCheckResult(assemblyList, issues);
        }

        private static HashSet<PeAssemblyInfo> CheckFormat(
            List<PeAssemblyInfo> assemblies,
            PeFormat? expectedFormat,
            List<CompatibilityIssue> issues)
        {
            var wrongFormat = new HashSet<PeAssemblyInfo>();

            if (assemblies.Count == 0)
            {
                return wrongFormat;
            }

            // when the expected format is unknown, a deployment mixing formats is an error:
            // the newest format is assumed to be the intended one (older PEs are leftovers from a previous toolchain)
            PeFormat requiredFormat = expectedFormat ?? assemblies.Max(a => a.Format);

            var reported = new HashSet<string>(StringComparer.Ordinal);

            foreach (PeAssemblyInfo assembly in assemblies.Where(a => a.Format != requiredFormat))
            {
                wrongFormat.Add(assembly);

                if (reported.Add($"{assembly.Name}|{assembly.Version}|{assembly.Format}|{assembly.SourcePath}"))
                {
                    issues.Add(new CompatibilityIssue(
                        CompatibilityIssueKind.PeFormatMismatch,
                        assembly,
                        assembly.Name,
                        requiredFormat: requiredFormat));
                }
            }

            return wrongFormat;
        }

        private static void CheckNative(
            List<PeAssemblyInfo> assemblies,
            IEnumerable<NativeAssemblyDescriptor> nativeAssemblies,
            List<CompatibilityIssue> issues)
        {
            // there can be more than one entry with the same name (e.g. variants)
            ILookup<string, NativeAssemblyDescriptor> nativeByName = nativeAssemblies.ToLookup(a => a.Name, StringComparer.Ordinal);

            // same assembly can show up more than once (e.g. in several images)
            var checkedAssemblies = new HashSet<string>(StringComparer.Ordinal);

            foreach (PeAssemblyInfo assembly in assemblies)
            {
                if (assembly.NativeHash == 0
                    || !checkedAssemblies.Add($"{assembly.Name}|{assembly.Version}|{assembly.NativeHash}"))
                {
                    // assemblies with native hash 0 don't require native support
                    continue;
                }

                List<NativeAssemblyDescriptor> candidates = nativeByName[assembly.Name].ToList();

                if (candidates.Count == 0)
                {
                    issues.Add(new CompatibilityIssue(
                        CompatibilityIssueKind.NativeAssemblyMissing,
                        assembly,
                        assembly.Name,
                        requiredNativeHash: assembly.NativeHash));
                }
                else if (!candidates.Any(c => c.ContractHash == assembly.NativeHash))
                {
                    issues.Add(new CompatibilityIssue(
                        CompatibilityIssueKind.NativeContractHashMismatch,
                        assembly,
                        assembly.Name,
                        requiredNativeHash: assembly.NativeHash,
                        actualNativeAssemblies: candidates));
                }
            }
        }

        private static void CheckReferences(
            List<PeAssemblyInfo> assemblies,
            List<PeAssemblyInfo> deployment,
            List<CompatibilityIssue> issues)
        {
            ILookup<string, PeAssemblyInfo> assembliesByName = deployment.ToLookup(a => a.Name, StringComparer.Ordinal);
            var checkedReferences = new HashSet<string>(StringComparer.Ordinal);

            foreach (PeAssemblyInfo assembly in assemblies)
            {
                foreach (PeAssemblyReference reference in assembly.References)
                {
                    if (!checkedReferences.Add($"{assembly.Name}|{assembly.Version}|{reference.Name}|{reference.Version}"))
                    {
                        continue;
                    }

                    List<Version> available = assembliesByName[reference.Name]
                        .Select(a => a.Version)
                        .Distinct()
                        .ToList();

                    if (available.Count == 0)
                    {
                        issues.Add(new CompatibilityIssue(
                            CompatibilityIssueKind.ReferenceMissing,
                            assembly,
                            reference.Name,
                            requiredVersion: reference.Version));
                    }
                    else if (!available.Any(v => v.Major == reference.Version.Major && v.Minor == reference.Version.Minor))
                    {
                        issues.Add(new CompatibilityIssue(
                            CompatibilityIssueKind.ReferenceVersionMismatch,
                            assembly,
                            reference.Name,
                            requiredVersion: reference.Version,
                            actualVersions: available));
                    }
                }
            }
        }
    }
}
