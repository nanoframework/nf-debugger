//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace nanoFramework.Tools.Debugger.Compatibility
{
    /// <summary>
    /// Kind of compatibility issue.
    /// </summary>
    public enum CompatibilityIssueKind
    {
        /// <summary>
        /// The PE has a format which can't be loaded by the firmware, or which differs from the rest of the deployment.
        /// </summary>
        PeFormatMismatch,

        /// <summary>
        /// The assembly requires a native counterpart which is not present in the firmware.
        /// </summary>
        NativeAssemblyMissing,

        /// <summary>
        /// The native counterpart in the firmware implements a different native contract (hash mismatch).
        /// </summary>
        NativeContractHashMismatch,

        /// <summary>
        /// The referenced assembly is part of the deployment, but with a different major.minor version.
        /// </summary>
        ReferenceVersionMismatch,

        /// <summary>
        /// The referenced assembly is not part of the deployment.
        /// </summary>
        ReferenceMissing,
    }

    /// <summary>
    /// A compatibility issue found by <see cref="DeploymentCompatibility"/>.
    /// </summary>
    public sealed class CompatibilityIssue
    {
        /// <summary>
        /// Kind of issue.
        /// </summary>
        public CompatibilityIssueKind Kind { get; }

        /// <summary>
        /// Assembly with the issue.
        /// </summary>
        public PeAssemblyInfo Assembly { get; }

        /// <summary>
        /// Name of the required assembly: the assembly itself for native issues, the referenced assembly for reference issues.
        /// </summary>
        public string RequiredName { get; }

        /// <summary>
        /// Required version of the referenced assembly. Only for reference issues (major.minor must match).
        /// </summary>
        public Version RequiredVersion { get; }

        /// <summary>
        /// Required native contract hash. Only for native issues.
        /// </summary>
        public uint? RequiredNativeHash { get; }

        /// <summary>
        /// Required PE format. Only for <see cref="CompatibilityIssueKind.PeFormatMismatch"/> (the actual format is the <see cref="PeAssemblyInfo.Format"/> of <see cref="Assembly"/>).
        /// </summary>
        public PeFormat? RequiredFormat { get; }

        /// <summary>
        /// Native contract hash(es) implemented by the firmware. Only for <see cref="CompatibilityIssueKind.NativeContractHashMismatch"/>.
        /// </summary>
        public IReadOnlyList<NativeAssemblyDescriptor> ActualNativeAssemblies { get; }

        /// <summary>
        /// Versions of the referenced assembly available in the deployment. Only for <see cref="CompatibilityIssueKind.ReferenceVersionMismatch"/>.
        /// </summary>
        public IReadOnlyList<Version> ActualVersions { get; }

        /// <summary>
        /// Path of the PE file or deployment image with the assembly that has the issue.
        /// </summary>
        public string SourcePath => Assembly.SourcePath;

        /// <summary>
        /// Creates a new <see cref="CompatibilityIssue"/>.
        /// </summary>
        public CompatibilityIssue(
            CompatibilityIssueKind kind,
            PeAssemblyInfo assembly,
            string requiredName,
            Version requiredVersion = null,
            uint? requiredNativeHash = null,
            IReadOnlyList<NativeAssemblyDescriptor> actualNativeAssemblies = null,
            IReadOnlyList<Version> actualVersions = null,
            PeFormat? requiredFormat = null)
        {
            Kind = kind;
            Assembly = assembly ?? throw new ArgumentNullException(nameof(assembly));
            RequiredName = requiredName ?? throw new ArgumentNullException(nameof(requiredName));
            RequiredVersion = requiredVersion;
            RequiredNativeHash = requiredNativeHash;
            ActualNativeAssemblies = actualNativeAssemblies ?? Array.Empty<NativeAssemblyDescriptor>();
            ActualVersions = actualVersions ?? Array.Empty<Version>();
            RequiredFormat = requiredFormat;
        }

        /// <summary>
        /// One line description of the issue.
        /// </summary>
        public string Description
        {
            get
            {
                switch (Kind)
                {
                    case CompatibilityIssueKind.PeFormatMismatch:
                        return $"'{Assembly.Name}' v{Assembly.Version} ({FormatLocation(Assembly)}) is a {FormatPeFormat(Assembly.Format)} PE, but {FormatPeFormat(RequiredFormat ?? Assembly.Format)} is required. {(RequiredFormat == PeFormat.V1 ? "Update the target firmware to a version with V2 support, or use packages matching the target firmware." : "Rebuild it with the v2 toolchain and packages.")}";

                    case CompatibilityIssueKind.NativeAssemblyMissing:
                        return $"'{Assembly.Name}' v{Assembly.Version} requires native contract hash 0x{RequiredNativeHash:X8}, but the target does not have support for '{RequiredName}'.";

                    case CompatibilityIssueKind.NativeContractHashMismatch:
                        return $"'{Assembly.Name}' v{Assembly.Version} requires native contract hash 0x{RequiredNativeHash:X8}, but the target has {FormatNative(ActualNativeAssemblies)}.";

                    case CompatibilityIssueKind.ReferenceVersionMismatch:
                        return $"'{Assembly.Name}' v{Assembly.Version} references '{RequiredName}' v{RequiredVersion} (requires v{RequiredVersion.Major}.{RequiredVersion.Minor}.*.*), but the deployment has {FormatVersions(RequiredName, ActualVersions)}.";

                    case CompatibilityIssueKind.ReferenceMissing:
                        return $"'{Assembly.Name}' v{Assembly.Version} references '{RequiredName}' v{RequiredVersion}, which is not part of the deployment.";

                    default:
                        return Kind.ToString();
                }
            }
        }

        /// <inheritdoc/>
        public override string ToString() => Description;

        internal static string FormatNative(IEnumerable<NativeAssemblyDescriptor> nativeAssemblies) =>
            string.Join(
                " or ",
                nativeAssemblies.Select(a => a.Version is null
                    ? $"contract hash 0x{a.ContractHash:X8}"
                    : $"v{a.Version}, contract hash 0x{a.ContractHash:X8}"));

        internal static string FormatPeFormat(PeFormat format) =>
            format == PeFormat.V1 ? "V1 (NFMRK1)" : "V2 (NFMRK2)";

        internal static string FormatLocation(PeAssemblyInfo assembly)
        {
            string path = assembly.SourcePath ?? "<memory>";

            // assemblies inside a deployment image are identified by their offset
            return assembly.Offset == 0 ? path : $"{path} @ 0x{assembly.Offset:X}";
        }

        private static string FormatVersions(string name, IEnumerable<Version> versions) =>
            string.Join(" and ", versions.Select(v => $"'{name}' v{v}"));
    }

    /// <summary>
    /// Result of a compatibility check performed by <see cref="DeploymentCompatibility"/>.
    /// </summary>
    public sealed class CompatibilityCheckResult
    {
        private const string Separator = "***************************************************************************";

        /// <summary>
        /// Link to the troubleshooting guide.
        /// </summary>
        public const string TroubleshootingGuideUrl = "https://docs.nanoframework.net/content/faq/working-with-vs-extension.html";

        /// <summary>
        /// Assemblies that were checked.
        /// </summary>
        public IReadOnlyList<PeAssemblyInfo> Assemblies { get; }

        /// <summary>
        /// Issues found. Empty if the deployment is compatible.
        /// </summary>
        public IReadOnlyList<CompatibilityIssue> Issues { get; }

        /// <summary>
        /// <see langword="true"/> if no issues were found.
        /// </summary>
        public bool IsCompatible => Issues.Count == 0;

        /// <summary>
        /// Creates a new <see cref="CompatibilityCheckResult"/>.
        /// </summary>
        public CompatibilityCheckResult(IReadOnlyList<PeAssemblyInfo> assemblies, IReadOnlyList<CompatibilityIssue> issues)
        {
            Assemblies = assemblies ?? throw new ArgumentNullException(nameof(assemblies));
            Issues = issues ?? throw new ArgumentNullException(nameof(issues));
        }

        /// <summary>
        /// Formats a human readable message describing the issues.
        /// </summary>
        /// <returns>The message, or an empty string if the deployment is compatible.</returns>
        public string FormatMessage()
        {
            if (IsCompatible)
            {
                return string.Empty;
            }

            string nl = Environment.NewLine;
            var message = new StringBuilder();

            message.Append("Deploy failed.").Append(nl).Append(nl);
            message.Append(Separator).Append(nl).Append(nl);

            var wrongFormat = Issues.Where(i => i.Kind == CompatibilityIssueKind.PeFormatMismatch).ToList();
            var wrongNative = Issues.Where(i => i.Kind == CompatibilityIssueKind.NativeContractHashMismatch).ToList();
            var missingNative = Issues.Where(i => i.Kind == CompatibilityIssueKind.NativeAssemblyMissing).ToList();
            var references = Issues.Where(i => i.Kind == CompatibilityIssueKind.ReferenceVersionMismatch
                                               || i.Kind == CompatibilityIssueKind.ReferenceMissing).ToList();

            if (wrongFormat.Count > 0)
            {
                message.Append("The target can't load the following PE file(s), they have the wrong format:").Append(nl).Append(nl);

                foreach (CompatibilityIssue issue in wrongFormat)
                {
                    message.Append($"    '{issue.Assembly.Name}' v{issue.Assembly.Version} is {CompatibilityIssue.FormatPeFormat(issue.Assembly.Format)}, {CompatibilityIssue.FormatPeFormat(issue.RequiredFormat ?? issue.Assembly.Format)} is required: {CompatibilityIssue.FormatLocation(issue.Assembly)}").Append(nl);
                }

                message.Append(nl);

                if (wrongFormat.Any(i => i.RequiredFormat == PeFormat.V1))
                {
                    // V2 PEs sent to V1 firmware
                    message.Append("The target is running firmware that only loads V1 (NFMRK1) PE files. Please update the target firmware to a version with V2 (NFMRK2) support, or build the project(s) with the nanoFramework NuGet packages and toolchain matching the target firmware.").Append(nl).Append(nl);
                }
                else
                {
                    message.Append("Please rebuild with the v2 toolchain: update the nanoFramework NuGet packages referenced by the project(s) to versions built for v2 firmware, make sure the Visual Studio/VS Code extension and the nanoFramework build components are up to date, then rebuild the solution.").Append(nl).Append(nl);
                }
            }

            if (wrongNative.Count > 0)
            {
                message.Append("The target has the wrong version for the following assembly(ies):").Append(nl).Append(nl);

                foreach (CompatibilityIssue issue in wrongNative)
                {
                    message.Append($"    '{issue.Assembly.Name}' v{issue.Assembly.Version} requires native contract hash 0x{issue.RequiredNativeHash:X8}.").Append(nl);
                    message.Append($"    Target has {CompatibilityIssue.FormatNative(issue.ActualNativeAssemblies)}.").Append(nl).Append(nl);
                }

                message.Append("Please check:").Append(nl);
                message.Append("  1) if the target is running the most updated image.").Append(nl);
                message.Append("  2) if the project is referring the appropriate version of the NuGet package.").Append(nl).Append(nl);
            }

            if (missingNative.Count > 0)
            {
                message.Append("The target does not have support for the following assembly(ies):").Append(nl).Append(nl);

                foreach (CompatibilityIssue issue in missingNative)
                {
                    message.Append($"    '{issue.Assembly.Name}' v{issue.Assembly.Version} (requires native contract hash 0x{issue.RequiredNativeHash:X8})").Append(nl);
                }

                message.Append(nl);
                message.Append("Please check:").Append(nl);
                message.Append("  1) if the target is running the most updated image.").Append(nl);
                message.Append("  2) if the target image was built to include support for all referenced assemblies.").Append(nl).Append(nl);
            }

            if (references.Count > 0)
            {
                message.Append("The following assembly reference(s) can't be resolved with the assemblies being deployed:").Append(nl).Append(nl);

                foreach (CompatibilityIssue issue in references)
                {
                    message.Append("    ").Append(issue.Description).Append(nl);
                }

                message.Append(nl);
                message.Append("Please check if the NuGet packages referenced by the project(s) are consistent: each referenced assembly must be deployed with the same major and minor version.").Append(nl).Append(nl);
            }

            message.Append($"Our Visual Studio FAQ has a troubleshooting guide: {TroubleshootingGuideUrl}").Append(nl).Append(nl);
            message.Append(Separator).Append(nl);

            return message.ToString();
        }
    }
}
