//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.Debugger.WireProtocol;
using QueryCapabilities = nanoFramework.Tools.Debugger.WireProtocol.Commands.Debugging_Execution_QueryCLRCapabilities;

namespace nanoFramework.Tools.Debugger.Tests
{
    [TestClass]
    public class NativeAssembliesWireFormatTests
    {
        private static readonly (string Name, uint Hash, Version Version)[] s_assemblies =
        {
            ("mscorlib", 0x532E6934, new Version(1, 17, 11, 0)),
            ("nanoFramework.Runtime.Native", 0x109F6F22, new Version(100, 0, 10, 0)),
            ("nanoFramework.Runtime.Events", 0x774AB407, new Version(1, 0, 8, 0)),
            ("System.Net", 0x5AD7808D, new Version(100, 2, 0, 9)),
        };

        [TestMethod]
        public void RecordSizes()
        {
            Assert.AreEqual(132, QueryCapabilities.NativeAssemblyDetailsV2.Size);
            Assert.AreEqual(140, QueryCapabilities.NativeAssemblyDetails.Size);
        }

        [TestMethod]
        public void RecordSizeDetection()
        {
            Assert.AreEqual(132, QueryCapabilities.GetNativeAssemblyRecordSize(132, 1));
            Assert.AreEqual(140, QueryCapabilities.GetNativeAssemblyRecordSize(140, 1));
            Assert.AreEqual(132, QueryCapabilities.GetNativeAssemblyRecordSize(132 * 7, 7));
            Assert.AreEqual(140, QueryCapabilities.GetNativeAssemblyRecordSize(140 * 7, 7));
            Assert.AreEqual(0, QueryCapabilities.GetNativeAssemblyRecordSize(136, 1));
            Assert.AreEqual(0, QueryCapabilities.GetNativeAssemblyRecordSize(0, 0));
        }

        [TestMethod]
        public void ParseV2Records()
        {
            byte[] data = BuildV2(s_assemblies);

            List<CLRCapabilities.NativeAssemblyProperties> parsed = QueryCapabilities.ParseNativeAssemblies(
                data,
                QueryCapabilities.GetNativeAssemblyRecordSize(data.Length, s_assemblies.Length));

            Assert.AreEqual(s_assemblies.Length, parsed.Count);

            for (int i = 0; i < s_assemblies.Length; i++)
            {
                Assert.AreEqual(s_assemblies[i].Name, parsed[i].Name);
                Assert.AreEqual(s_assemblies[i].Hash, parsed[i].Checksum);
                Assert.IsNull(parsed[i].Version);
            }
        }

        [TestMethod]
        public void ParseV1Records()
        {
            byte[] data = BuildV1(s_assemblies);

            List<CLRCapabilities.NativeAssemblyProperties> parsed = QueryCapabilities.ParseNativeAssemblies(
                data,
                QueryCapabilities.GetNativeAssemblyRecordSize(data.Length, s_assemblies.Length));

            Assert.AreEqual(s_assemblies.Length, parsed.Count);

            for (int i = 0; i < s_assemblies.Length; i++)
            {
                Assert.AreEqual(s_assemblies[i].Name, parsed[i].Name);
                Assert.AreEqual(s_assemblies[i].Hash, parsed[i].Checksum);
                Assert.AreEqual(s_assemblies[i].Version, parsed[i].Version);
            }
        }

        [TestMethod]
        public void FormatV2Assembly()
        {
            string line = new CLRCapabilities.NativeAssemblyProperties("mscorlib", 0x532E6934, null).ToString();

            Assert.AreEqual("mscorlib, contract hash 0x532E6934", line);
            Assert.IsFalse(line.Contains(" v"));
        }

        [TestMethod]
        public void FormatV1Assembly()
        {
            string line = new CLRCapabilities.NativeAssemblyProperties("mscorlib", 0x2D5CA905, new Version(100, 5, 0, 19)).ToString();

            Assert.AreEqual("mscorlib v100.5.0.19, checksum 0x2D5CA905", line);
        }

        private static byte[] BuildV2((string Name, uint Hash, Version Version)[] assemblies)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            foreach ((string name, uint hash, _) in assemblies)
            {
                writer.Write(hash);
                writer.Write(NameBuffer(name));
            }

            return stream.ToArray();
        }

        private static byte[] BuildV1((string Name, uint Hash, Version Version)[] assemblies)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            foreach ((string name, uint hash, Version version) in assemblies)
            {
                writer.Write(hash);
                writer.Write((ushort)version.Major);
                writer.Write((ushort)version.Minor);
                writer.Write((ushort)version.Build);
                writer.Write((ushort)version.Revision);
                writer.Write(NameBuffer(name));
            }

            return stream.ToArray();
        }

        private static byte[] NameBuffer(string name)
        {
            byte[] buffer = new byte[128];
            Encoding.UTF8.GetBytes(name).CopyTo(buffer, 0);

            return buffer;
        }
    }
}
