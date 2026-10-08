//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace nanoFramework.Tools.Debugger.Compatibility
{
    /// <summary>
    /// List of the native assemblies included in a firmware package (native_assemblies.json).
    /// </summary>
    /// <remarks>
    /// Expected schema:
    /// <code>
    /// {
    ///   "schemaVersion": 1,
    ///   "target": "ESP32_C3",
    ///   "nanoCLRVersion": "2.0.1.0",
    ///   "nativeAssemblies": [ { "name": "System.Device.Gpio", "contractHash": "0x1234ABCD", "variant": "optional" } ]
    /// }
    /// </code>
    /// Unknown properties are ignored.
    /// </remarks>
    public sealed class NativeAssembliesManifest
    {
        /// <summary>
        /// Default file name of the manifest in a firmware package.
        /// </summary>
        public const string DefaultFileName = "native_assemblies.json";

        /// <summary>
        /// Version of the manifest schema.
        /// </summary>
        public int SchemaVersion { get; }

        /// <summary>
        /// Name of the target the firmware was built for.
        /// </summary>
        public string Target { get; }

        /// <summary>
        /// Version of nanoCLR in the firmware.
        /// </summary>
        public string NanoClrVersion { get; }

        /// <summary>
        /// Native assemblies included in the firmware.
        /// </summary>
        public IReadOnlyList<NativeAssemblyDescriptor> NativeAssemblies { get; }

        /// <summary>
        /// PE format required by the firmware.
        /// </summary>
        /// <remarks>
        /// Firmware packages with a native assemblies manifest are built from nanoCLR with generics support, which only loads <see cref="PeFormat.V2"/> PEs.
        /// </remarks>
        public PeFormat ExpectedPeFormat => PeFormat.V2;

        /// <summary>
        /// Creates a new <see cref="NativeAssembliesManifest"/>.
        /// </summary>
        public NativeAssembliesManifest(
            int schemaVersion,
            string target,
            string nanoClrVersion,
            IReadOnlyList<NativeAssemblyDescriptor> nativeAssemblies)
        {
            SchemaVersion = schemaVersion;
            Target = target;
            NanoClrVersion = nanoClrVersion;
            NativeAssemblies = nativeAssemblies ?? throw new ArgumentNullException(nameof(nativeAssemblies));
        }

        /// <summary>
        /// Loads a manifest from a file.
        /// </summary>
        /// <param name="path">Path to the native_assemblies.json file.</param>
        /// <exception cref="InvalidDataException">The file content is not a valid manifest.</exception>
        public static NativeAssembliesManifest Load(string path)
        {
            if (path is null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            return Parse(File.ReadAllText(path), path);
        }

        /// <summary>
        /// Parses the JSON content of a manifest.
        /// </summary>
        /// <param name="json">JSON content.</param>
        /// <param name="sourcePath">Optional path of the source, used in error messages.</param>
        /// <exception cref="InvalidDataException">The content is not a valid manifest.</exception>
        public static NativeAssembliesManifest Parse(string json, string sourcePath = null)
        {
            if (json is null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            string source = sourcePath ?? "native assemblies manifest";
            ManifestData data;

            try
            {
                var serializer = new DataContractJsonSerializer(typeof(ManifestData));

                using (var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(json.TrimStart('﻿'))))
                {
                    data = (ManifestData)serializer.ReadObject(stream);
                }
            }
            catch (Exception ex) when (ex is SerializationException || ex is System.Xml.XmlException)
            {
                throw new InvalidDataException($"'{source}' is not a valid native assemblies manifest: {ex.Message}", ex);
            }

            if (data is null)
            {
                throw new InvalidDataException($"'{source}' is empty.");
            }

            if (data.SchemaVersion < 1)
            {
                throw new InvalidDataException($"'{source}' has a missing or invalid 'schemaVersion'.");
            }

            if (data.NativeAssemblies is null)
            {
                throw new InvalidDataException($"'{source}' is missing the 'nativeAssemblies' list.");
            }

            var nativeAssemblies = new List<NativeAssemblyDescriptor>(data.NativeAssemblies.Count);

            for (int i = 0; i < data.NativeAssemblies.Count; i++)
            {
                AssemblyData assembly = data.NativeAssemblies[i];

                if (assembly is null || string.IsNullOrWhiteSpace(assembly.Name))
                {
                    throw new InvalidDataException($"'{source}': entry #{i} in 'nativeAssemblies' has no 'name'.");
                }

                if (!TryParseHash(assembly.ContractHash, out uint contractHash))
                {
                    throw new InvalidDataException($"'{source}': '{assembly.Name}' has an invalid 'contractHash' ('{assembly.ContractHash}'). Expected format is \"0xXXXXXXXX\".");
                }

                nativeAssemblies.Add(new NativeAssemblyDescriptor(
                    assembly.Name,
                    contractHash,
                    null,
                    string.IsNullOrEmpty(assembly.Variant) ? null : assembly.Variant));
            }

            return new NativeAssembliesManifest(data.SchemaVersion, data.Target, data.NanoClrVersion, nativeAssemblies);
        }

        private static bool TryParseHash(string value, out uint hash)
        {
            hash = 0;

            if (value is null)
            {
                return false;
            }

            value = value.Trim();

            if (value.Length < 3
                || value.Length > 10
                || value[0] != '0'
                || (value[1] != 'x' && value[1] != 'X'))
            {
                return false;
            }

            return uint.TryParse(value.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out hash);
        }

        [DataContract]
        internal sealed class ManifestData
        {
            [DataMember(Name = "schemaVersion")]
            public int SchemaVersion { get; set; }

            [DataMember(Name = "target")]
            public string Target { get; set; }

            [DataMember(Name = "nanoCLRVersion")]
            public string NanoClrVersion { get; set; }

            [DataMember(Name = "nativeAssemblies")]
            public List<AssemblyData> NativeAssemblies { get; set; }
        }

        [DataContract]
        internal sealed class AssemblyData
        {
            [DataMember(Name = "name")]
            public string Name { get; set; }

            [DataMember(Name = "contractHash")]
            public string ContractHash { get; set; }

            [DataMember(Name = "variant")]
            public string Variant { get; set; }
        }
    }
}
