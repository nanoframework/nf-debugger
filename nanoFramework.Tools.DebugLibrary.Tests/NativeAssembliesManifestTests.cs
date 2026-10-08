//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.IO;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.Debugger.Compatibility;

namespace nanoFramework.Tools.Debugger.Tests
{
    [TestClass]
    public class NativeAssembliesManifestTests
    {
        private const string ValidManifest = @"{
  ""schemaVersion"": 1,
  ""target"": ""ESP32_C3"",
  ""nanoCLRVersion"": ""1.12.0.0"",
  ""someFutureProperty"": { ""ignored"": true },
  ""nativeAssemblies"": [
    { ""name"": ""mscorlib"", ""contractHash"": ""0x2D5CA905"" },
    { ""name"": ""nanoFramework.Graphics"", ""contractHash"": ""0xabcdef01"", ""variant"": ""Generic"" }
  ]
}";

        [TestMethod]
        public void ParsesValidManifest()
        {
            NativeAssembliesManifest manifest = NativeAssembliesManifest.Parse(ValidManifest);

            Assert.AreEqual(1, manifest.SchemaVersion);
            Assert.AreEqual("ESP32_C3", manifest.Target);
            Assert.AreEqual("1.12.0.0", manifest.NanoClrVersion);
            Assert.AreEqual(2, manifest.NativeAssemblies.Count);

            Assert.AreEqual("mscorlib", manifest.NativeAssemblies[0].Name);
            Assert.AreEqual(0x2D5CA905u, manifest.NativeAssemblies[0].ContractHash);
            Assert.IsNull(manifest.NativeAssemblies[0].Variant);
            Assert.IsNull(manifest.NativeAssemblies[0].Version);

            Assert.AreEqual("nanoFramework.Graphics", manifest.NativeAssemblies[1].Name);
            Assert.AreEqual(0xABCDEF01u, manifest.NativeAssemblies[1].ContractHash);
            Assert.AreEqual("Generic", manifest.NativeAssemblies[1].Variant);
        }

        [TestMethod]
        public void LoadsFileWithBom()
        {
            string path = Path.Combine(Path.GetTempPath(), $"native_assemblies-{Guid.NewGuid():N}.json");
            File.WriteAllText(path, ValidManifest, new UTF8Encoding(true));

            try
            {
                NativeAssembliesManifest manifest = NativeAssembliesManifest.Load(path);
                Assert.AreEqual(2, manifest.NativeAssemblies.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }

        [DataTestMethod]
        [DataRow("12345678")]
        [DataRow("0x")]
        [DataRow("0x123456789")]
        [DataRow("0xGHIJKLMN")]
        [DataRow("")]
        public void InvalidContractHashThrows(string hash)
        {
            string json = $"{{ \"schemaVersion\": 1, \"nativeAssemblies\": [ {{ \"name\": \"A\", \"contractHash\": \"{hash}\" }} ] }}";

            InvalidDataException ex = Assert.ThrowsException<InvalidDataException>(() => NativeAssembliesManifest.Parse(json, "fw/native_assemblies.json"));
            StringAssert.Contains(ex.Message, "'fw/native_assemblies.json': 'A' has an invalid 'contractHash'");
        }

        [TestMethod]
        public void MissingContractHashThrows()
        {
            Assert.ThrowsException<InvalidDataException>(
                () => NativeAssembliesManifest.Parse("{ \"schemaVersion\": 1, \"nativeAssemblies\": [ { \"name\": \"A\" } ] }"));
        }

        [TestMethod]
        public void MissingNameThrows()
        {
            Assert.ThrowsException<InvalidDataException>(
                () => NativeAssembliesManifest.Parse("{ \"schemaVersion\": 1, \"nativeAssemblies\": [ { \"contractHash\": \"0x1\" } ] }"));
        }

        [TestMethod]
        public void MissingListThrows()
        {
            InvalidDataException ex = Assert.ThrowsException<InvalidDataException>(
                () => NativeAssembliesManifest.Parse("{ \"schemaVersion\": 1 }"));

            StringAssert.Contains(ex.Message, "nativeAssemblies");
        }

        [TestMethod]
        public void MissingSchemaVersionThrows()
        {
            InvalidDataException ex = Assert.ThrowsException<InvalidDataException>(
                () => NativeAssembliesManifest.Parse("{ \"nativeAssemblies\": [] }"));

            StringAssert.Contains(ex.Message, "schemaVersion");
        }

        [DataTestMethod]
        [DataRow("not json")]
        [DataRow("{ \"schemaVersion\": \"one\", \"nativeAssemblies\": [] }")]
        [DataRow("[ 1, 2 ]")]
        public void InvalidJsonThrows(string json)
        {
            Assert.ThrowsException<InvalidDataException>(() => NativeAssembliesManifest.Parse(json));
        }
    }
}
