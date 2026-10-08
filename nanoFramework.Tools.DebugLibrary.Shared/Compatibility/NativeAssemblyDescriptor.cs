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
    /// Native assembly available in a firmware, as reported by a connected device or listed in a firmware package.
    /// </summary>
    public sealed class NativeAssemblyDescriptor
    {
        /// <summary>
        /// Name of the assembly.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Hash of the native contract implemented by the firmware (native methods checksum).
        /// </summary>
        public uint ContractHash { get; }

        /// <summary>
        /// Version of the native assembly, if known. Informational only, not used for compatibility checks.
        /// </summary>
        public Version Version { get; }

        /// <summary>
        /// Variant of the native implementation, if any. Informational only, not used for compatibility checks.
        /// </summary>
        public string Variant { get; }

        /// <summary>
        /// Creates a new <see cref="NativeAssemblyDescriptor"/>.
        /// </summary>
        public NativeAssemblyDescriptor(string name, uint contractHash, Version version = null, string variant = null)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            ContractHash = contractHash;
            Version = version;
            Variant = variant;
        }

        /// <summary>
        /// Converts the native assemblies reported by a connected device.
        /// </summary>
        /// <param name="nativeAssemblies">Native assemblies reported by the device (<see cref="CLRCapabilities.NativeAssemblies"/>).</param>
        public static IReadOnlyList<NativeAssemblyDescriptor> FromDevice(IEnumerable<CLRCapabilities.NativeAssemblyProperties> nativeAssemblies)
        {
            if (nativeAssemblies is null)
            {
                throw new ArgumentNullException(nameof(nativeAssemblies));
            }

            return nativeAssemblies
                .Where(a => a.Name != null)
                .Select(a => new NativeAssemblyDescriptor(a.Name, a.Checksum, a.Version))
                .ToList();
        }

        /// <inheritdoc/>
        public override string ToString() => $"{Name} 0x{ContractHash:X8}";
    }
}
