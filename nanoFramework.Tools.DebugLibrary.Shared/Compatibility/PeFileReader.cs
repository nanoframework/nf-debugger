//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace nanoFramework.Tools.Debugger.Compatibility
{
    /// <summary>
    /// Reads the header and assembly references of nanoFramework PE files and deployment images.
    /// </summary>
    /// <remarks>
    /// A deployment image is a concatenation of PE files, each one padded to a 4 bytes boundary.
    /// Layouts follow CLR_RECORD_ASSEMBLY and CLR_RECORD_ASSEMBLYREF in nf-interpreter src/CLR/Include/nanoCLR_Types.h.
    /// </remarks>
    public static class PeFileReader
    {
        private const int MarkerLength = 6;

        // common header fields
        private const int NativeHashOffset = 20;

        // V1 (NFMRK1) header layout
        // marker[8], headerCRC, assemblyCRC, flags, nativeMethodsChecksum, patchEntryOffset,
        // version, assemblyName, stringTableVersion, startOfTables[16], numOfPatchedMethods, paddingOfTables[16]
        private const int V1VersionOffset = 28;
        private const int V1AssemblyNameOffset = 36;
        private const int V1StartOfTablesOffset = 40;
        private const int V1TableCount = 16;
        private const int V1PaddingOfTablesOffset = V1StartOfTablesOffset + V1TableCount * 4 + 4;
        private const int V1HeaderSize = V1PaddingOfTablesOffset + 16;
        private const int V1TableStrings = 11;
        private const int V1TableEndOfAssembly = 15;
        private const int V1AssemblyRefSize = 12;
        private const int V1AssemblyRefVersionOffset = 4;

        // V2 (NFMRK2) header layout
        // marker[8], headerCRC, assemblyCRC, flags, nativeMethodsChecksum,
        // version, assemblyName, stringTableVersion, startOfTables[18], paddingOfTables[20]
        private const int V2VersionOffset = 24;
        private const int V2AssemblyNameOffset = 32;
        private const int V2StartOfTablesOffset = 36;
        private const int V2TableCount = 18;
        private const int V2PaddingOfTablesOffset = V2StartOfTablesOffset + V2TableCount * 4;
        private const int V2HeaderSize = V2PaddingOfTablesOffset + 20;
        private const int V2TableStrings = 13;
        private const int V2TableEndOfAssembly = 17;
        private const int V2AssemblyRefSize = 10;
        private const int V2AssemblyRefVersionOffset = 2;

        // TBL_AssemblyRef is the first table in both formats
        private const int TableAssemblyRef = 0;

        // nanoCLR built-in string table (src/CLR/Core/StringTableData.cpp, c_CLR_StringTable_Version 1):
        // a string index from 0xFFFF - c_CLR_StringTable_Size up refers to entry 0xFFFF - index
        private const int SharedStringTableSize = 0x35C;
        private const int SharedStringsFirstIndex = 0xFFFF - SharedStringTableSize;

        // "mscorlib" is entry 0x32D
        private const ushort SharedStringMscorlibIndex = 0xFFFF - 0x32D;

        private static readonly byte[] s_markerV1 = Encoding.ASCII.GetBytes("NFMRK1");
        private static readonly byte[] s_markerV2 = Encoding.ASCII.GetBytes("NFMRK2");

        /// <summary>
        /// Reads all the assemblies in a PE file or deployment image file.
        /// </summary>
        /// <param name="path">Path to a PE file or to a deployment image.</param>
        /// <returns>The assemblies found in the file, in the order they are stored.</returns>
        /// <exception cref="InvalidDataException">The file is not a valid nanoFramework PE file or deployment image.</exception>
        public static IReadOnlyList<PeAssemblyInfo> ReadFile(string path)
        {
            if (path is null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            return Read(File.ReadAllBytes(path), path);
        }

        /// <summary>
        /// Reads all the assemblies in the content of a PE file or deployment image.
        /// </summary>
        /// <param name="data">Content of a PE file or of a deployment image.</param>
        /// <param name="sourcePath">Optional path of the source, used in the returned information and in error messages.</param>
        /// <returns>The assemblies found, in the order they are stored.</returns>
        /// <exception cref="InvalidDataException">The data is not a valid nanoFramework PE file or deployment image.</exception>
        public static IReadOnlyList<PeAssemblyInfo> Read(
            byte[] data,
            string sourcePath = null)
        {
            if (data is null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            string source = sourcePath ?? "<memory>";
            var assemblies = new List<PeAssemblyInfo>();
            int offset = 0;

            while (offset < data.Length)
            {
                if (!TryGetFormat(data, offset, out PeFormat format))
                {
                    // after at least one assembly, trailing erased flash (0xFF) or zero padding is accepted
                    if (assemblies.Count > 0 && IsFiller(data, offset))
                    {
                        break;
                    }

                    throw new InvalidDataException(
                        assemblies.Count == 0
                            ? $"'{source}' is not a nanoFramework PE file or deployment image (no assembly marker found)."
                            : $"'{source}': unexpected data at offset 0x{offset:X} after assembly '{assemblies[assemblies.Count - 1].Name}' (no assembly marker found).");
                }

                PeAssemblyInfo assembly = ReadAssembly(
                    data,
                    offset,
                    format,
                    source,
                    sourcePath);
                assemblies.Add(assembly);

                // assemblies in a deployment image are aligned to 4 bytes
                long next = ((long)offset + assembly.Size + 3) & ~3L;
                offset = next >= data.Length ? data.Length : (int)next;
            }

            if (assemblies.Count == 0)
            {
                throw new InvalidDataException($"'{source}' is empty.");
            }

            return assemblies;
        }

        private static PeAssemblyInfo ReadAssembly(
            byte[] data,
            int offset,
            PeFormat format,
            string source,
            string sourcePath)
        {
            bool isV1 = format == PeFormat.V1;
            int headerSize = isV1 ? V1HeaderSize : V2HeaderSize;
            int tableCount = isV1 ? V1TableCount : V2TableCount;
            int startOfTablesOffset = isV1 ? V1StartOfTablesOffset : V2StartOfTablesOffset;
            int paddingOfTablesOffset = isV1 ? V1PaddingOfTablesOffset : V2PaddingOfTablesOffset;
            int stringsTable = isV1 ? V1TableStrings : V2TableStrings;
            int endOfAssemblyTable = isV1 ? V1TableEndOfAssembly : V2TableEndOfAssembly;

            if (data.Length - offset < headerSize)
            {
                throw new InvalidDataException($"'{source}': assembly at offset 0x{offset:X} is truncated (header requires {headerSize} bytes, {data.Length - offset} available).");
            }

            // table start offsets are relative to the start of the assembly
            var startOfTables = new uint[tableCount];

            for (int i = 0; i < tableCount; i++)
            {
                startOfTables[i] = ReadUInt32(data, offset + startOfTablesOffset + i * 4);
            }

            uint totalSize = startOfTables[endOfAssemblyTable];

            if (totalSize < headerSize || totalSize > (uint)(data.Length - offset))
            {
                throw new InvalidDataException($"'{source}': assembly at offset 0x{offset:X} declares an invalid size ({totalSize} bytes, {data.Length - offset} available).");
            }

            for (int i = 0; i < endOfAssemblyTable; i++)
            {
                if (startOfTables[i] < headerSize
                    || startOfTables[i] > startOfTables[i + 1])
                {
                    throw new InvalidDataException($"'{source}': assembly at offset 0x{offset:X} has an invalid table layout (table {i} starts at 0x{startOfTables[i]:X}).");
                }
            }

            int size = (int)totalSize;
            uint nativeHash = ReadUInt32(data, offset + NativeHashOffset);
            Version version = ReadVersion(data, offset + (isV1 ? V1VersionOffset : V2VersionOffset));
            ushort nameIndex = ReadUInt16(data, offset + (isV1 ? V1AssemblyNameOffset : V2AssemblyNameOffset));

            // strings table bounds, relative to the start of the assembly
            int stringsStart = (int)startOfTables[stringsTable];
            int stringsEnd = (int)startOfTables[stringsTable + 1] - data[offset + paddingOfTablesOffset + stringsTable];

            string name = ReadString(
                data,
                offset,
                stringsStart,
                stringsEnd,
                nameIndex,
                source,
                "assembly name");

            // AssemblyRef table
            int assemblyRefSize = isV1 ? V1AssemblyRefSize : V2AssemblyRefSize;
            int assemblyRefVersionOffset = isV1 ? V1AssemblyRefVersionOffset : V2AssemblyRefVersionOffset;
            int assemblyRefStart = (int)startOfTables[TableAssemblyRef];
            int assemblyRefTableSize = (int)startOfTables[TableAssemblyRef + 1] - assemblyRefStart - data[offset + paddingOfTablesOffset + TableAssemblyRef];

            if (assemblyRefTableSize < 0 || assemblyRefTableSize % assemblyRefSize != 0)
            {
                throw new InvalidDataException($"'{source}': assembly '{name}' has an invalid AssemblyRef table size ({assemblyRefTableSize} bytes).");
            }

            var references = new List<PeAssemblyReference>(assemblyRefTableSize / assemblyRefSize);

            for (int row = 0; row < assemblyRefTableSize; row += assemblyRefSize)
            {
                int rowOffset = offset + assemblyRefStart + row;
                ushort refNameIndex = ReadUInt16(data, rowOffset);
                Version refVersion = ReadVersion(data, rowOffset + assemblyRefVersionOffset);
                string refName = ReadString(
                    data,
                    offset,
                    stringsStart,
                    stringsEnd,
                    refNameIndex,
                    source,
                    $"name of reference #{row / assemblyRefSize} in '{name}'");

                references.Add(new PeAssemblyReference(refName, refVersion));
            }

            return new PeAssemblyInfo(
                name,
                version,
                nativeHash,
                format,
                references,
                sourcePath,
                offset,
                size);
        }

        private static bool TryGetFormat(
            byte[] data,
            int offset,
            out PeFormat format)
        {
            if (StartsWith(data, offset, s_markerV2))
            {
                format = PeFormat.V2;
                return true;
            }

            if (StartsWith(data, offset, s_markerV1))
            {
                format = PeFormat.V1;
                return true;
            }

            format = default;
            return false;
        }

        private static bool StartsWith(
            byte[] data,
            int offset,
            byte[] marker)
        {
            if (data.Length - offset < MarkerLength)
            {
                return false;
            }

            for (int i = 0; i < MarkerLength; i++)
            {
                if (data[offset + i] != marker[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFiller(
            byte[] data,
            int offset)
        {
            byte filler = data[offset];

            if (filler != 0x00 && filler != 0xFF)
            {
                return false;
            }

            for (int i = offset; i < data.Length; i++)
            {
                if (data[i] != filler)
                {
                    return false;
                }
            }

            return true;
        }

        private static string ReadString(
            byte[] data,
            int assemblyOffset,
            int stringsStart,
            int stringsEnd,
            ushort index,
            string source,
            string what)
        {
            // same rule as CLR_RT_Assembly::GetString(): high indexes point to the nanoCLR built-in string table
            if (index >= SharedStringsFirstIndex)
            {
                // Developer notes:
                // - The metadata processor stores assembly names as plain strings, so the built-in table is not needed.
                // - V1 PEs (and V2 PEs built before that change) have mscorlib in the built-in table, that's the only one needed.
                if (index == SharedStringMscorlibIndex)
                {
                    return "mscorlib";
                }

                throw new InvalidDataException($"'{source}': {what} uses nanoCLR built-in string 0x{index:X4}, which is not supported for assembly names. Rebuild it with an updated metadata processor.");
            }

            int start = assemblyOffset + stringsStart + index;
            int end = assemblyOffset + stringsEnd;

            if (index >= stringsEnd - stringsStart)
            {
                throw new InvalidDataException($"'{source}': {what} has string index 0x{index:X4} outside of the strings table.");
            }

            int terminator = Array.IndexOf(data, (byte)0, start, end - start);

            if (terminator < 0)
            {
                throw new InvalidDataException($"'{source}': {what} is not terminated.");
            }

            return Encoding.UTF8.GetString(data, start, terminator - start);
        }

        private static Version ReadVersion(byte[] data, int offset) =>
            new Version(
                ReadUInt16(data, offset),
                ReadUInt16(data, offset + 2),
                ReadUInt16(data, offset + 4),
                ReadUInt16(data, offset + 6));

        private static ushort ReadUInt16(byte[] data, int offset) =>
            (ushort)(data[offset] | (data[offset + 1] << 8));

        private static uint ReadUInt32(byte[] data, int offset) =>
            (uint)(data[offset]
                | (data[offset + 1] << 8)
                | (data[offset + 2] << 16)
                | (data[offset + 3] << 24));
    }
}
