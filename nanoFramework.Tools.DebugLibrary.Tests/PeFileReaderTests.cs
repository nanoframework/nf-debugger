//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.Debugger.Compatibility;

namespace nanoFramework.Tools.Debugger.Tests
{
    [TestClass]
    public class PeFileReaderTests
    {
        internal static string Asset(string relativePath) =>
            Path.Combine(AppContext.BaseDirectory, "TestAssets", relativePath.Replace('\\', Path.DirectorySeparatorChar));

        [TestMethod]
        public void RealV2PeWithResources()
        {
            // regression: V1 table offsets applied to a V2 PE with resources produce a wrong size and name
            PeAssemblyInfo assembly = PeFileReader.ReadFile(Asset(@"V2\nanoFramework.M5StickC.pe")).Single();

            Assert.AreEqual(PeFormat.V2, assembly.Format);
            Assert.AreEqual("nanoFramework.M5StickC", assembly.Name);
            Assert.AreEqual(new Version(2, 0, 0, 0), assembly.Version);
            Assert.AreEqual(0u, assembly.NativeHash);
            Assert.AreEqual(0, assembly.Offset);
            Assert.AreEqual(7696, assembly.Size);
            Assert.AreEqual(14, assembly.References.Count);

            AssertReference(assembly, "mscorlib", new Version(2, 0, 0, 0));
            AssertReference(assembly, "nanoFramework.ResourceManager", new Version(2, 0, 0, 0));
            AssertReference(assembly, "nanoFramework.Hardware.Esp32.Rmt", new Version(3, 0, 0, 0));
            AssertReference(assembly, "nanoFramework.UnitsNet.ElectricPotential", new Version(5, 77, 0, 0));
        }

        [TestMethod]
        public void RealV2PeWithNativeHash()
        {
            PeAssemblyInfo assembly = PeFileReader.ReadFile(Asset(@"V2\nanoFramework.System.IO.Hashing.pe")).Single();

            Assert.AreEqual(PeFormat.V2, assembly.Format);
            Assert.AreEqual("nanoFramework.System.IO.Hashing", assembly.Name);
            Assert.AreEqual(new Version(2, 0, 0, 0), assembly.Version);
            Assert.AreEqual(0x639242D6u, assembly.NativeHash);
            Assert.AreEqual(1, assembly.References.Count);
            AssertReference(assembly, "mscorlib", new Version(2, 0, 0, 0));
        }

        [TestMethod]
        public void RealV1PeWithResources()
        {
            PeAssemblyInfo assembly = PeFileReader.ReadFile(Asset(@"V1\ManagedResources.pe")).Single();

            Assert.AreEqual(PeFormat.V1, assembly.Format);
            Assert.AreEqual("ManagedResources", assembly.Name);
            Assert.AreEqual(new Version(1, 0, 9753, 17329), assembly.Version);
            Assert.AreEqual(0u, assembly.NativeHash);
            Assert.AreEqual(2120, assembly.Size);
            Assert.AreEqual(2, assembly.References.Count);
            AssertReference(assembly, "mscorlib", new Version(1, 17, 11, 0));
            AssertReference(assembly, "nanoFramework.ResourceManager", new Version(1, 2, 32, 0));
        }

        [TestMethod]
        public void RealV1PeWithNativeHash()
        {
            PeAssemblyInfo assembly = PeFileReader.ReadFile(Asset(@"V1\nanoFramework.ResourceManager.pe")).Single();

            Assert.AreEqual(PeFormat.V1, assembly.Format);
            Assert.AreEqual("nanoFramework.ResourceManager", assembly.Name);
            Assert.AreEqual(new Version(1, 2, 32, 0), assembly.Version);
            Assert.AreEqual(0xDCD7DF4Du, assembly.NativeHash);
            AssertReference(assembly, "mscorlib", new Version(1, 17, 11, 0));
        }

        [TestMethod]
        public void RealV2DeploymentImage()
        {
            string path = Asset(@"V2\UnitTestLauncher.bin");
            IReadOnlyList<PeAssemblyInfo> assemblies = PeFileReader.ReadFile(path);

            CollectionAssert.AreEqual(
                new[] { "mscorlib", "nanoFramework.TestFramework", "UnitTestLauncher" },
                assemblies.Select(a => a.Name).ToArray());

            Assert.IsTrue(assemblies.All(a => a.Format == PeFormat.V2));
            Assert.IsTrue(assemblies.All(a => a.SourcePath == path));

            // mscorlib name comes from the shared string table
            Assert.AreEqual(new Version(2, 0, 0, 0), assemblies[0].Version);
            Assert.AreEqual(0x2D5CA905u, assemblies[0].NativeHash);
            Assert.AreEqual(0, assemblies[0].References.Count);

            Assert.AreEqual(45148, assemblies[1].Offset);
            Assert.AreEqual(0u, assemblies[1].NativeHash);
            AssertReference(assemblies[1], "mscorlib", new Version(2, 0, 0, 0));

            Assert.AreEqual(54680, assemblies[2].Offset);
            AssertReference(assemblies[2], "mscorlib", new Version(2, 0, 0, 0));
            AssertReference(assemblies[2], "nanoFramework.TestFramework", new Version(1, 0, 0, 0));
        }

        [DataTestMethod]
        [DataRow(PeFormat.V1, false)]
        [DataRow(PeFormat.V1, true)]
        [DataRow(PeFormat.V2, false)]
        [DataRow(PeFormat.V2, true)]
        public void SynthesizedPe(PeFormat format, bool withResources)
        {
            var builder = new PeBuilder(format, "My.Library", new Version(1, 2, 3, 4), 0xCAFEF00D) { WithResources = withResources }
                .Reference("mscorlib", new Version(1, 17, 0, 0))
                .Reference("Other.Library", new Version(5, 6, 7, 8));

            byte[] data = builder.Build();
            PeAssemblyInfo assembly = PeFileReader.Read(data).Single();

            Assert.AreEqual(format, assembly.Format);
            Assert.AreEqual("My.Library", assembly.Name);
            Assert.AreEqual(new Version(1, 2, 3, 4), assembly.Version);
            Assert.AreEqual(0xCAFEF00Du, assembly.NativeHash);
            Assert.AreEqual(data.Length, assembly.Size);
            Assert.IsNull(assembly.SourcePath);
            Assert.AreEqual(2, assembly.References.Count);
            AssertReference(assembly, "mscorlib", new Version(1, 17, 0, 0));
            AssertReference(assembly, "Other.Library", new Version(5, 6, 7, 8));
        }

        [DataTestMethod]
        [DataRow(PeFormat.V1)]
        [DataRow(PeFormat.V2)]
        public void SynthesizedDeploymentImageWithUnalignedAssemblies(PeFormat format)
        {
            var first = new PeBuilder(format, "First", new Version(1, 0, 0, 0)) { TrailingBytes = 1, WithResources = true };
            var second = new PeBuilder(format, "Second", new Version(2, 0, 0, 0), 0x12345678) { TrailingBytes = 2 }
                .Reference("First", new Version(1, 0, 0, 0));
            var third = new PeBuilder(format, "Third", new Version(3, 0, 0, 0)) { WithResources = true }
                .Reference("Second", new Version(2, 0, 0, 0));

            Assert.AreNotEqual(0, first.Build().Length % 4, "test requires an unaligned assembly");

            byte[] image = PeBuilder.BuildImage(first, second, third);
            IReadOnlyList<PeAssemblyInfo> assemblies = PeFileReader.Read(image, "image.bin");

            CollectionAssert.AreEqual(new[] { "First", "Second", "Third" }, assemblies.Select(a => a.Name).ToArray());
            Assert.AreEqual(0x12345678u, assemblies[1].NativeHash);
            Assert.AreEqual((first.Build().Length + 3) & ~3, assemblies[1].Offset);
            AssertReference(assemblies[2], "Second", new Version(2, 0, 0, 0));
            Assert.IsTrue(assemblies.All(a => a.SourcePath == "image.bin"));
        }

        [TestMethod]
        public void MixedFormatDeploymentImage()
        {
            byte[] image = PeBuilder.BuildImage(
                new PeBuilder(PeFormat.V1, "Old", new Version(1, 0, 0, 0)),
                new PeBuilder(PeFormat.V2, "New", new Version(2, 0, 0, 0)));

            IReadOnlyList<PeAssemblyInfo> assemblies = PeFileReader.Read(image);

            Assert.AreEqual(PeFormat.V1, assemblies[0].Format);
            Assert.AreEqual(PeFormat.V2, assemblies[1].Format);
        }

        [DataTestMethod]
        [DataRow((byte)0x00)]
        [DataRow((byte)0xFF)]
        public void TrailingFillerIsIgnored(byte filler)
        {
            byte[] pe = new PeBuilder(PeFormat.V2, "App", new Version(1, 0, 0, 0)).Build();
            byte[] data = pe.Concat(Enumerable.Repeat(filler, 64)).ToArray();

            Assert.AreEqual("App", PeFileReader.Read(data).Single().Name);
        }

        [TestMethod]
        public void TrailingGarbageThrows()
        {
            byte[] pe = PeBuilder.BuildImage(new PeBuilder(PeFormat.V2, "App", new Version(1, 0, 0, 0)));
            byte[] data = pe.Concat(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }).ToArray();

            InvalidDataException ex = Assert.ThrowsException<InvalidDataException>(() => PeFileReader.Read(data, "x.bin"));
            StringAssert.Contains(ex.Message, "after assembly 'App'");
        }

        [TestMethod]
        public void NotAPeThrows()
        {
            InvalidDataException ex = Assert.ThrowsException<InvalidDataException>(
                () => PeFileReader.Read(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0, 0, 0, 0 }, "app.dll"));

            StringAssert.Contains(ex.Message, "'app.dll' is not a nanoFramework PE file");
        }

        [TestMethod]
        public void EmptyDataThrows()
        {
            Assert.ThrowsException<InvalidDataException>(() => PeFileReader.Read(Array.Empty<byte>()));
        }

        [DataTestMethod]
        [DataRow(PeFormat.V1)]
        [DataRow(PeFormat.V2)]
        public void TruncatedPeThrows(PeFormat format)
        {
            byte[] pe = new PeBuilder(format, "App", new Version(1, 0, 0, 0)).Reference("mscorlib", new Version(1, 0, 0, 0)).Build();

            // truncated header
            Assert.ThrowsException<InvalidDataException>(() => PeFileReader.Read(pe.Take(60).ToArray()));

            // truncated body
            InvalidDataException ex = Assert.ThrowsException<InvalidDataException>(() => PeFileReader.Read(pe.Take(pe.Length - 4).ToArray()));
            StringAssert.Contains(ex.Message, "invalid size");
        }

        [TestMethod]
        public void InvalidStringIndexThrows()
        {
            byte[] pe = new PeBuilder(PeFormat.V2, "App", new Version(1, 0, 0, 0)).Build();

            // assembly name index outside of the strings table
            pe[32] = 0x00;
            pe[33] = 0x40;

            InvalidDataException ex = Assert.ThrowsException<InvalidDataException>(() => PeFileReader.Read(pe));
            StringAssert.Contains(ex.Message, "outside of the strings table");
        }

        [TestMethod]
        public void BuiltInMscorlibStringIsResolved()
        {
            byte[] pe = new PeBuilder(PeFormat.V2, "App", new Version(1, 0, 0, 0)).Build();

            // assembly name pointing to "mscorlib" in the nanoCLR built-in string table (V2 PEs from older metadata processor)
            pe[32] = 0xD2;
            pe[33] = 0xFC;

            Assert.AreEqual("mscorlib", PeFileReader.Read(pe).Single().Name);
        }

        [TestMethod]
        public void OtherBuiltInStringThrows()
        {
            byte[] pe = new PeBuilder(PeFormat.V2, "App", new Version(1, 0, 0, 0)).Build();

            // assembly name pointing to "System" (entry 0x1BF) in the nanoCLR built-in string table
            pe[32] = 0x40;
            pe[33] = 0xFE;

            InvalidDataException ex = Assert.ThrowsException<InvalidDataException>(() => PeFileReader.Read(pe));
            StringAssert.Contains(ex.Message, "built-in string 0xFE40");
        }

        [TestMethod]
        public void InvalidTableLayoutThrows()
        {
            byte[] pe = new PeBuilder(PeFormat.V2, "App", new Version(1, 0, 0, 0)).Build();

            // TypeRef table starting before the header end
            BitConverter.GetBytes(8u).CopyTo(pe, 36 + 4);

            InvalidDataException ex = Assert.ThrowsException<InvalidDataException>(() => PeFileReader.Read(pe));
            StringAssert.Contains(ex.Message, "invalid table layout");
        }

        private static void AssertReference(PeAssemblyInfo assembly, string name, Version version)
        {
            PeAssemblyReference reference = assembly.References.SingleOrDefault(r => r.Name == name);

            Assert.IsNotNull(reference, $"'{assembly.Name}' doesn't reference '{name}'.");
            Assert.AreEqual(version, reference.Version, $"Version of reference to '{name}' in '{assembly.Name}'.");
        }
    }
}
