//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using nanoFramework.Tools.Debugger.Compatibility;

namespace nanoFramework.Tools.Debugger.Tests
{
    /// <summary>
    /// Builds byte exact nanoFramework PE files (V1 and V2 layouts) for tests.
    /// </summary>
    internal sealed class PeBuilder
    {
        private readonly List<(string Name, Version Version)> _references = new List<(string, Version)>();

        public PeBuilder(PeFormat format, string name, Version version, uint nativeHash = 0)
        {
            Format = format;
            Name = name;
            Version = version;
            NativeHash = nativeHash;
        }

        public PeFormat Format { get; }

        public string Name { get; }

        public Version Version { get; }

        public uint NativeHash { get; }

        /// <summary>
        /// Adds content to the Resources, ResourcesData and ResourcesFiles tables.
        /// </summary>
        public bool WithResources { get; set; }

        /// <summary>
        /// Extra bytes added to the last table (ResourcesFiles), to produce assembly sizes not multiple of 4.
        /// </summary>
        public int TrailingBytes { get; set; }

        public PeBuilder Reference(string name, Version version)
        {
            _references.Add((name, version));
            return this;
        }

        public byte[] Build()
        {
            bool isV1 = Format == PeFormat.V1;

            int tableCount = isV1 ? 16 : 18;
            int endOfAssembly = tableCount - 1;
            int startOfTablesOffset = isV1 ? 40 : 36;
            int paddingOfTablesOffset = startOfTablesOffset + tableCount * 4 + (isV1 ? 4 : 0);
            int headerSize = paddingOfTablesOffset + (isV1 ? 16 : 20);

            int tblResources = isV1 ? 9 : 11;
            int tblResourcesData = isV1 ? 10 : 12;
            int tblStrings = isV1 ? 11 : 13;
            int tblByteCode = isV1 ? 13 : 15;
            int tblResourcesFiles = isV1 ? 14 : 16;

            // strings: name, then references names
            var strings = new MemoryStream();
            var stringIndexes = new Dictionary<string, ushort>();

            ushort AddString(string value)
            {
                if (isV1 && value == "mscorlib")
                {
                    // V1 PEs use the nanoCLR built-in string table entry, V2 ones have the plain string
                    return 0xFCD2;
                }

                if (!stringIndexes.TryGetValue(value, out ushort index))
                {
                    index = (ushort)strings.Length;
                    byte[] bytes = Encoding.UTF8.GetBytes(value);
                    strings.Write(bytes, 0, bytes.Length);
                    strings.WriteByte(0);
                    stringIndexes[value] = index;
                }

                return index;
            }

            ushort nameIndex = AddString(Name);

            var assemblyRefs = new MemoryStream();

            foreach ((string refName, Version refVersion) in _references)
            {
                WriteUInt16(assemblyRefs, AddString(refName));

                if (isV1)
                {
                    // pad
                    WriteUInt16(assemblyRefs, 0);
                }

                WriteVersion(assemblyRefs, refVersion);
            }

            var tables = new byte[endOfAssembly][];

            for (int i = 0; i < endOfAssembly; i++)
            {
                tables[i] = Array.Empty<byte>();
            }

            tables[0] = assemblyRefs.ToArray();
            tables[tblStrings] = strings.ToArray();
            tables[tblByteCode] = Enumerable.Repeat((byte)0x2A, 3).ToArray();

            if (WithResources)
            {
                tables[tblResources] = Enumerable.Repeat((byte)0x11, 16).ToArray();
                tables[tblResourcesData] = Encoding.UTF8.GetBytes("resource data that is not a string table\0");
                tables[tblResourcesFiles] = Enumerable.Repeat((byte)0x22, 24).ToArray();
            }

            tables[tblResourcesFiles] = tables[tblResourcesFiles].Concat(Enumerable.Repeat((byte)0x33, TrailingBytes)).ToArray();

            var output = new MemoryStream();
            output.Write(new byte[headerSize], 0, headerSize);

            var startOfTables = new uint[tableCount];
            var paddingOfTables = new byte[tableCount - 1];

            for (int i = 0; i < endOfAssembly; i++)
            {
                startOfTables[i] = (uint)output.Length;
                output.Write(tables[i], 0, tables[i].Length);

                // the last table (ResourcesFiles) isn't padded, so the total size can be unaligned
                int padding = i == endOfAssembly - 1 ? 0 : (int)((4 - (output.Length % 4)) % 4);
                output.Write(new byte[padding], 0, padding);
                paddingOfTables[i] = (byte)padding;
            }

            startOfTables[endOfAssembly] = (uint)output.Length;

            byte[] data = output.ToArray();

            Encoding.ASCII.GetBytes(isV1 ? "NFMRK1" : "NFMRK2").CopyTo(data, 0);
            BitConverter.GetBytes(NativeHash).CopyTo(data, 20);

            int versionOffset = isV1 ? 28 : 24;
            BitConverter.GetBytes((ushort)Version.Major).CopyTo(data, versionOffset);
            BitConverter.GetBytes((ushort)Version.Minor).CopyTo(data, versionOffset + 2);
            BitConverter.GetBytes((ushort)Version.Build).CopyTo(data, versionOffset + 4);
            BitConverter.GetBytes((ushort)Version.Revision).CopyTo(data, versionOffset + 6);
            BitConverter.GetBytes(nameIndex).CopyTo(data, versionOffset + 8);
            BitConverter.GetBytes((ushort)1).CopyTo(data, versionOffset + 10);

            for (int i = 0; i < tableCount; i++)
            {
                BitConverter.GetBytes(startOfTables[i]).CopyTo(data, startOfTablesOffset + i * 4);
            }

            paddingOfTables.CopyTo(data, paddingOfTablesOffset);

            return data;
        }

        /// <summary>
        /// Builds a deployment image: assemblies concatenated, each one padded to 4 bytes.
        /// </summary>
        public static byte[] BuildImage(params PeBuilder[] assemblies)
        {
            var output = new MemoryStream();

            foreach (PeBuilder assembly in assemblies)
            {
                byte[] data = assembly.Build();
                output.Write(data, 0, data.Length);
                int padding = (4 - (data.Length % 4)) % 4;
                output.Write(new byte[padding], 0, padding);
            }

            return output.ToArray();
        }

        private static void WriteVersion(Stream stream, Version version)
        {
            WriteUInt16(stream, (ushort)version.Major);
            WriteUInt16(stream, (ushort)version.Minor);
            WriteUInt16(stream, (ushort)version.Build);
            WriteUInt16(stream, (ushort)version.Revision);
        }

        private static void WriteUInt16(Stream stream, ushort value)
        {
            stream.WriteByte((byte)value);
            stream.WriteByte((byte)(value >> 8));
        }
    }
}
