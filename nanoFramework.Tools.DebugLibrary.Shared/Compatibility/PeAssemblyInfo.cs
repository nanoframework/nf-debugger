//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;

namespace nanoFramework.Tools.Debugger.Compatibility
{
    /// <summary>
    /// Format of a nanoFramework PE (assembly) file.
    /// </summary>
    public enum PeFormat
    {
        /// <summary>
        /// Legacy format, with "NFMRK1" marker.
        /// </summary>
        V1 = 1,

        /// <summary>
        /// Current format (with generics support), with "NFMRK2" marker.
        /// </summary>
        V2 = 2,
    }

    /// <summary>
    /// Reference from a nanoFramework assembly to another assembly (entry of the AssemblyRef table).
    /// </summary>
    public sealed class PeAssemblyReference
    {
        /// <summary>
        /// Name of the referenced assembly.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Version of the referenced assembly.
        /// </summary>
        public Version Version { get; }

        /// <summary>
        /// Creates a new <see cref="PeAssemblyReference"/>.
        /// </summary>
        public PeAssemblyReference(string name, Version version)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Version = version ?? throw new ArgumentNullException(nameof(version));
        }

        /// <inheritdoc/>
        public override string ToString() => $"{Name} v{Version}";
    }

    /// <summary>
    /// Information about a nanoFramework assembly read from a PE file or from a deployment image.
    /// </summary>
    public sealed class PeAssemblyInfo
    {
        /// <summary>
        /// Name of the assembly.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Version of the assembly.
        /// </summary>
        public Version Version { get; }

        /// <summary>
        /// Hash of the native contract (native methods checksum) required by this assembly.
        /// 0 means the assembly doesn't require a native counterpart.
        /// </summary>
        public uint NativeHash { get; }

        /// <summary>
        /// Format of the PE.
        /// </summary>
        public PeFormat Format { get; }

        /// <summary>
        /// Assemblies referenced by this assembly.
        /// </summary>
        public IReadOnlyList<PeAssemblyReference> References { get; }

        /// <summary>
        /// Path of the file this assembly was read from. Can be <see langword="null"/> if read from memory.
        /// </summary>
        public string SourcePath { get; }

        /// <summary>
        /// Offset of this assembly in the source file (non zero for assemblies in a deployment image).
        /// </summary>
        public int Offset { get; }

        /// <summary>
        /// Size of this assembly, in bytes, as declared in the header (not including alignment padding).
        /// </summary>
        public int Size { get; }

        /// <summary>
        /// Creates a new <see cref="PeAssemblyInfo"/>.
        /// </summary>
        public PeAssemblyInfo(
            string name,
            Version version,
            uint nativeHash,
            PeFormat format,
            IReadOnlyList<PeAssemblyReference> references,
            string sourcePath = null,
            int offset = 0,
            int size = 0)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Version = version ?? throw new ArgumentNullException(nameof(version));
            NativeHash = nativeHash;
            Format = format;
            References = references ?? Array.Empty<PeAssemblyReference>();
            SourcePath = sourcePath;
            Offset = offset;
            Size = size;
        }

        /// <inheritdoc/>
        public override string ToString() => $"{Name} v{Version}";
    }
}
