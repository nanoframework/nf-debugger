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
    public class DeploymentCompatibilityTests
    {
        private static readonly Version s_v1 = new Version(1, 0, 0, 0);

        [TestMethod]
        public void CompatibleDeployment()
        {
            var assemblies = Read(
                new PeBuilder(PeFormat.V2, "mscorlib", new Version(2, 0, 0, 0), 0x11111111),
                new PeBuilder(PeFormat.V2, "System.Device.Gpio", new Version(2, 1, 0, 0), 0x22222222)
                    .Reference("mscorlib", new Version(2, 0, 0, 0)),
                new PeBuilder(PeFormat.V2, "App", s_v1)
                    .Reference("mscorlib", new Version(2, 0, 0, 0))
                    .Reference("System.Device.Gpio", new Version(2, 1, 0, 0)));

            CompatibilityCheckResult result = DeploymentCompatibility.Check(
                assemblies,
                new[]
                {
                    new NativeAssemblyDescriptor("mscorlib", 0x11111111),
                    new NativeAssemblyDescriptor("System.Device.Gpio", 0x22222222),
                    new NativeAssemblyDescriptor("Not.Used", 0x33333333),
                });

            Assert.IsTrue(result.IsCompatible);
            Assert.AreEqual(0, result.Issues.Count);
            Assert.AreEqual(3, result.Assemblies.Count);
            Assert.AreEqual(string.Empty, result.FormatMessage());
        }

        [TestMethod]
        public void NativeHashMismatch()
        {
            var assemblies = Read(new PeBuilder(PeFormat.V2, "System.Device.Gpio", new Version(1, 2, 3, 4), 0x12345678));

            CompatibilityCheckResult result = DeploymentCompatibility.Check(
                assemblies,
                new[] { new NativeAssemblyDescriptor("System.Device.Gpio", 0x87654321, new Version(1, 0, 0, 0)) });

            CompatibilityIssue issue = result.Issues.Single();
            Assert.IsFalse(result.IsCompatible);
            Assert.AreEqual(CompatibilityIssueKind.NativeContractHashMismatch, issue.Kind);
            Assert.AreEqual("System.Device.Gpio", issue.RequiredName);
            Assert.AreEqual(0x12345678u, issue.RequiredNativeHash);
            Assert.AreEqual(0x87654321u, issue.ActualNativeAssemblies.Single().ContractHash);
            Assert.AreSame(assemblies[0], issue.Assembly);

            string message = result.FormatMessage();
            StringAssert.Contains(message, "wrong version");
            StringAssert.Contains(message, "0x12345678");
            StringAssert.Contains(message, "0x87654321");
            StringAssert.Contains(message, CompatibilityCheckResult.TroubleshootingGuideUrl);
        }

        [TestMethod]
        public void NativeAssemblyMissing()
        {
            var assemblies = Read(new PeBuilder(PeFormat.V2, "System.Device.Gpio", new Version(1, 2, 3, 4), 0x12345678));

            CompatibilityCheckResult result = DeploymentCompatibility.Check(assemblies, Array.Empty<NativeAssemblyDescriptor>());

            CompatibilityIssue issue = result.Issues.Single();
            Assert.AreEqual(CompatibilityIssueKind.NativeAssemblyMissing, issue.Kind);
            Assert.AreEqual(0x12345678u, issue.RequiredNativeHash);

            string message = result.FormatMessage();
            StringAssert.Contains(message, "does not have support");
            StringAssert.Contains(message, "'System.Device.Gpio'");
            StringAssert.Contains(message, "https://docs.nanoframework.net/content/faq/working-with-vs-extension.html");
        }

        [TestMethod]
        public void ManagedOnlyAssemblyDoesNotNeedNativeSupport()
        {
            var assemblies = Read(new PeBuilder(PeFormat.V2, "MyApplication", s_v1));

            Assert.IsTrue(DeploymentCompatibility.Check(assemblies, Array.Empty<NativeAssemblyDescriptor>()).IsCompatible);
        }

        [TestMethod]
        public void NativeVersionIsNotCompared()
        {
            // AssemblyNativeVersion is gone: only the contract hash matters
            var assemblies = Read(new PeBuilder(PeFormat.V2, "System.Device.Gpio", new Version(2, 0, 0, 0), 0x12345678));

            CompatibilityCheckResult result = DeploymentCompatibility.Check(
                assemblies,
                new[] { new NativeAssemblyDescriptor("System.Device.Gpio", 0x12345678, new Version(100, 1, 0, 0)) });

            Assert.IsTrue(result.IsCompatible);
        }

        [TestMethod]
        public void NativeVariantsWithSameNameAreAccepted()
        {
            var assemblies = Read(new PeBuilder(PeFormat.V2, "nanoFramework.Graphics", s_v1, 0x22222222));

            CompatibilityCheckResult result = DeploymentCompatibility.Check(
                assemblies,
                new[]
                {
                    new NativeAssemblyDescriptor("nanoFramework.Graphics", 0x11111111, variant: "A"),
                    new NativeAssemblyDescriptor("nanoFramework.Graphics", 0x22222222, variant: "B"),
                });

            Assert.IsTrue(result.IsCompatible);
        }

        [TestMethod]
        public void ReferenceMajorMinorMismatch()
        {
            var assemblies = Read(
                new PeBuilder(PeFormat.V2, "Library", new Version(1, 3, 0, 0)),
                new PeBuilder(PeFormat.V2, "App", s_v1).Reference("Library", new Version(1, 2, 5, 6)));

            CompatibilityCheckResult result = DeploymentCompatibility.Check(assemblies, Array.Empty<NativeAssemblyDescriptor>());

            CompatibilityIssue issue = result.Issues.Single();
            Assert.AreEqual(CompatibilityIssueKind.ReferenceVersionMismatch, issue.Kind);
            Assert.AreEqual("App", issue.Assembly.Name);
            Assert.AreEqual("Library", issue.RequiredName);
            Assert.AreEqual(new Version(1, 2, 5, 6), issue.RequiredVersion);
            Assert.AreEqual(new Version(1, 3, 0, 0), issue.ActualVersions.Single());

            string message = result.FormatMessage();
            StringAssert.Contains(message, "'App' v1.0.0.0 references 'Library' v1.2.5.6");
            StringAssert.Contains(message, "'Library' v1.3.0.0");
        }

        [TestMethod]
        public void ReferenceBuildAndRevisionDifferencesAreAccepted()
        {
            // same rule as CLR_RT_TypeSystem::FindAssembly with fExact = false
            var assemblies = Read(
                new PeBuilder(PeFormat.V2, "Library", new Version(1, 2, 9, 9)),
                new PeBuilder(PeFormat.V2, "App", s_v1).Reference("Library", new Version(1, 2, 0, 0)));

            Assert.IsTrue(DeploymentCompatibility.Check(assemblies, Array.Empty<NativeAssemblyDescriptor>()).IsCompatible);
        }

        [TestMethod]
        public void ReferenceMissing()
        {
            var assemblies = Read(
                new PeBuilder(PeFormat.V2, "App", s_v1)
                    .Reference("mscorlib", new Version(2, 0, 0, 0))
                    .Reference("Missing.Library", new Version(1, 0, 0, 0)));

            CompatibilityCheckResult result = DeploymentCompatibility.Check(assemblies, Array.Empty<NativeAssemblyDescriptor>());

            Assert.AreEqual(2, result.Issues.Count);
            Assert.IsTrue(result.Issues.All(i => i.Kind == CompatibilityIssueKind.ReferenceMissing));
            CollectionAssert.AreEquivalent(new[] { "mscorlib", "Missing.Library" }, result.Issues.Select(i => i.RequiredName).ToArray());

            StringAssert.Contains(result.FormatMessage(), "'Missing.Library' v1.0.0.0, which is not part of the deployment");
        }

        [TestMethod]
        public void DuplicatedAssembliesReportIssuesOnce()
        {
            var gpio = new PeBuilder(PeFormat.V2, "System.Device.Gpio", s_v1, 0x12345678).Reference("Missing", s_v1);
            var assemblies = Read(gpio, gpio);

            CompatibilityCheckResult result = DeploymentCompatibility.Check(assemblies, Array.Empty<NativeAssemblyDescriptor>());

            Assert.AreEqual(1, result.Issues.Count(i => i.Kind == CompatibilityIssueKind.NativeAssemblyMissing));
            Assert.AreEqual(1, result.Issues.Count(i => i.Kind == CompatibilityIssueKind.ReferenceMissing));
        }

        [TestMethod]
        public void AllIssueKindsInOneMessage()
        {
            var assemblies = Read(
                new PeBuilder(PeFormat.V2, "Wrong.Native", s_v1, 0x1),
                new PeBuilder(PeFormat.V2, "Missing.Native", s_v1, 0x2),
                new PeBuilder(PeFormat.V2, "App", s_v1).Reference("Wrong.Native", new Version(2, 0, 0, 0)).Reference("Nowhere", s_v1));

            CompatibilityCheckResult result = DeploymentCompatibility.Check(
                assemblies,
                new[] { new NativeAssemblyDescriptor("Wrong.Native", 0x3) });

            CollectionAssert.AreEquivalent(
                new[]
                {
                    CompatibilityIssueKind.NativeContractHashMismatch,
                    CompatibilityIssueKind.NativeAssemblyMissing,
                    CompatibilityIssueKind.ReferenceVersionMismatch,
                    CompatibilityIssueKind.ReferenceMissing,
                },
                result.Issues.Select(i => i.Kind).ToArray());

            string message = result.FormatMessage();
            StringAssert.StartsWith(message, "Deploy failed.");
            StringAssert.Contains(message, "wrong version");
            StringAssert.Contains(message, "does not have support");
            StringAssert.Contains(message, "can't be resolved");
        }

        [TestMethod]
        public void DeviceNativeAssembliesOverload()
        {
            string path = WriteTempFile(PeBuilder.BuildImage(
                new PeBuilder(PeFormat.V2, "mscorlib", new Version(2, 0, 0, 0), 0xAAAAAAAA),
                new PeBuilder(PeFormat.V2, "App", s_v1).Reference("mscorlib", new Version(2, 0, 0, 0))));

            try
            {
                var device = new List<CLRCapabilities.NativeAssemblyProperties>
                {
                    new CLRCapabilities.NativeAssemblyProperties("mscorlib", 0xAAAAAAAA, new Version(100, 0, 0, 0)),
                };

                CompatibilityCheckResult result = DeploymentCompatibility.Check(new[] { path }, device, PeFormat.V2);

                Assert.IsTrue(result.IsCompatible);
                Assert.AreEqual(path, result.Assemblies[0].SourcePath);

                device[0] = new CLRCapabilities.NativeAssemblyProperties("mscorlib", 0xBBBBBBBB, new Version(100, 0, 0, 0));
                result = DeploymentCompatibility.Check(new[] { path }, device, PeFormat.V2);

                CompatibilityIssue issue = result.Issues.Single();
                Assert.AreEqual(CompatibilityIssueKind.NativeContractHashMismatch, issue.Kind);
                Assert.AreEqual(path, issue.SourcePath);
                StringAssert.Contains(result.FormatMessage(), "v100.0.0.0, contract hash 0xBBBBBBBB");
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void RealDeploymentImage()
        {
            string path = PeFileReaderTests.Asset(@"V2\UnitTestLauncher.bin");

            CompatibilityCheckResult result = DeploymentCompatibility.Check(
                new[] { path },
                new[] { new NativeAssemblyDescriptor("mscorlib", 0x2D5CA905) });

            Assert.IsTrue(result.IsCompatible, result.FormatMessage());

            result = DeploymentCompatibility.Check(
                new[] { path },
                new[] { new NativeAssemblyDescriptor("mscorlib", 0xC337B934) });

            Assert.AreEqual(CompatibilityIssueKind.NativeContractHashMismatch, result.Issues.Single().Kind);
        }

        [TestMethod]
        public void RealPeFilesWithMissingReferences()
        {
            // the M5StickC library references assemblies which are not part of this "deployment"
            string m5 = PeFileReaderTests.Asset(@"V2\nanoFramework.M5StickC.pe");
            string hashing = PeFileReaderTests.Asset(@"V2\nanoFramework.System.IO.Hashing.pe");
            string image = PeFileReaderTests.Asset(@"V2\UnitTestLauncher.bin");

            CompatibilityCheckResult result = DeploymentCompatibility.Check(
                new[] { image, hashing, m5 },
                new[]
                {
                    new NativeAssemblyDescriptor("mscorlib", 0x2D5CA905),
                    new NativeAssemblyDescriptor("nanoFramework.System.IO.Hashing", 0x639242D6),
                });

            Assert.IsTrue(result.Issues.All(i => i.Kind == CompatibilityIssueKind.ReferenceMissing && i.Assembly.Name == "nanoFramework.M5StickC"));
            Assert.AreEqual(13, result.Issues.Count);
            Assert.IsTrue(result.Issues.All(i => i.SourcePath == m5));
        }

        [TestMethod]
        public void ManifestAsNativeAssembliesSource()
        {
            NativeAssembliesManifest manifest = NativeAssembliesManifest.Parse(
                "{ \"schemaVersion\": 1, \"target\": \"ESP32_S3\", \"nanoCLRVersion\": \"2.0.0.100\", " +
                "\"nativeAssemblies\": [ { \"name\": \"mscorlib\", \"contractHash\": \"0x2D5CA905\" } ] }");

            CompatibilityCheckResult result = DeploymentCompatibility.Check(
                new[] { PeFileReaderTests.Asset(@"V2\UnitTestLauncher.bin") },
                manifest);

            Assert.IsTrue(result.IsCompatible, result.FormatMessage());
        }

        [TestMethod]
        public void ManifestRequiresV2Pes()
        {
            NativeAssembliesManifest manifest = NativeAssembliesManifest.Parse(
                "{ \"schemaVersion\": 1, \"nativeAssemblies\": [ { \"name\": \"nanoFramework.ResourceManager\", \"contractHash\": \"0xDCD7DF4D\" } ] }");

            CompatibilityCheckResult result = DeploymentCompatibility.Check(
                new[] { PeFileReaderTests.Asset(@"V1\nanoFramework.ResourceManager.pe") },
                manifest);

            CompatibilityIssue issue = result.Issues.Single();
            Assert.AreEqual(CompatibilityIssueKind.PeFormatMismatch, issue.Kind);
            Assert.AreEqual(PeFormat.V2, issue.RequiredFormat);
        }

        [TestMethod]
        public void MixedFormatsWithoutExpectedFormat()
        {
            var assemblies = Read(
                new PeBuilder(PeFormat.V1, "mscorlib", new Version(1, 17, 0, 0), 0x11111111),
                new PeBuilder(PeFormat.V1, "Old.Library", s_v1, 0x22222222).Reference("mscorlib", new Version(1, 17, 0, 0)),
                new PeBuilder(PeFormat.V2, "App", s_v1)
                    .Reference("mscorlib", new Version(1, 17, 0, 0))
                    .Reference("Old.Library", s_v1));

            // no native assemblies: the V1 PEs must not be reported as missing native support, only as wrong format
            CompatibilityCheckResult result = DeploymentCompatibility.Check(assemblies, Array.Empty<NativeAssemblyDescriptor>());

            Assert.AreEqual(2, result.Issues.Count);
            Assert.IsTrue(result.Issues.All(i => i.Kind == CompatibilityIssueKind.PeFormatMismatch && i.RequiredFormat == PeFormat.V2));
            CollectionAssert.AreEquivalent(new[] { "mscorlib", "Old.Library" }, result.Issues.Select(i => i.Assembly.Name).ToArray());
        }

        [TestMethod]
        public void SingleFormatWithoutExpectedFormatIsAccepted()
        {
            var assemblies = Read(
                new PeBuilder(PeFormat.V1, "mscorlib", new Version(1, 17, 0, 0), 0x11111111),
                new PeBuilder(PeFormat.V1, "App", s_v1).Reference("mscorlib", new Version(1, 17, 0, 0)));

            CompatibilityCheckResult result = DeploymentCompatibility.Check(
                assemblies,
                new[] { new NativeAssemblyDescriptor("mscorlib", 0x11111111) });

            Assert.IsTrue(result.IsCompatible, result.FormatMessage());
        }

        [TestMethod]
        public void ExpectedFormatFlagsEveryOtherPe()
        {
            var assemblies = Read(
                new PeBuilder(PeFormat.V1, "mscorlib", new Version(1, 17, 0, 0), 0x11111111),
                new PeBuilder(PeFormat.V1, "App", s_v1).Reference("mscorlib", new Version(1, 17, 0, 0)));

            CompatibilityCheckResult result = DeploymentCompatibility.Check(
                assemblies,
                new[] { new NativeAssemblyDescriptor("mscorlib", 0x11111111) },
                PeFormat.V2);

            Assert.AreEqual(2, result.Issues.Count);
            Assert.IsTrue(result.Issues.All(i => i.Kind == CompatibilityIssueKind.PeFormatMismatch));

            // and V1 can still be required explicitly
            Assert.IsTrue(DeploymentCompatibility.Check(
                assemblies,
                new[] { new NativeAssemblyDescriptor("mscorlib", 0x11111111) },
                PeFormat.V1).IsCompatible);
        }

        [DataTestMethod]
        [DataRow("1.18.4.10", PeFormat.V1)]
        [DataRow("1.0.0.0", PeFormat.V1)]
        [DataRow("2.0.1.0", PeFormat.V2)]
        [DataRow("2.3.0.150", PeFormat.V2)]
        [DataRow("3.0.0.0", PeFormat.V2)]
        public void PeFormatFromNanoClrVersion(string nanoClrVersion, PeFormat expected)
        {
            Assert.AreEqual(expected, DeploymentCompatibility.GetDevicePeFormat(Version.Parse(nanoClrVersion)));
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("0.0.0.0")]
        public void UnknownNanoClrVersionHasNoPeFormat(string nanoClrVersion)
        {
            Assert.IsNull(DeploymentCompatibility.GetDevicePeFormat(nanoClrVersion is null ? null : Version.Parse(nanoClrVersion)));

            // no engine or no capabilities (e.g. connected to nanoBooter)
            Assert.IsNull(DeploymentCompatibility.GetDevicePeFormat((Engine)null));
        }

        [TestMethod]
        public void UnknownFirmwareOnlyReportsMixedFormats()
        {
            var device = new List<CLRCapabilities.NativeAssemblyProperties>
            {
                new CLRCapabilities.NativeAssemblyProperties("mscorlib", 0x1549C856, new Version(100, 5, 0, 0)),
            };

            string v1Only = WriteTempFile(PeBuilder.BuildImage(
                new PeBuilder(PeFormat.V1, "mscorlib", new Version(1, 17, 11, 0), 0x1549C856),
                new PeBuilder(PeFormat.V1, "App", s_v1).Reference("mscorlib", new Version(1, 17, 11, 0))));

            string mixed = WriteTempFile(PeBuilder.BuildImage(
                new PeBuilder(PeFormat.V1, "mscorlib", new Version(1, 17, 11, 0), 0x1549C856),
                new PeBuilder(PeFormat.V2, "App", s_v1).Reference("mscorlib", new Version(1, 17, 11, 0))));

            try
            {
                PeFormat? unknown = DeploymentCompatibility.GetDevicePeFormat(new Version(0, 0, 0, 0));

                Assert.IsTrue(DeploymentCompatibility.Check(new[] { v1Only }, device, unknown).IsCompatible);

                CompatibilityCheckResult result = DeploymentCompatibility.Check(new[] { mixed }, device, unknown);
                CompatibilityIssue issue = result.Issues.Single();
                Assert.AreEqual(CompatibilityIssueKind.PeFormatMismatch, issue.Kind);
                Assert.AreEqual("mscorlib", issue.Assembly.Name);
            }
            finally
            {
                File.Delete(v1Only);
                File.Delete(mixed);
            }
        }

        [TestMethod]
        public void V1FirmwareAcceptsAllV1Bundle()
        {
            // nanoCLR 1.x: V1 firmware
            string path = WriteTempFile(PeBuilder.BuildImage(
                new PeBuilder(PeFormat.V1, "mscorlib", new Version(1, 17, 11, 0), 0x1549C856),
                new PeBuilder(PeFormat.V1, "App", s_v1).Reference("mscorlib", new Version(1, 17, 11, 0))));

            try
            {
                var device = new List<CLRCapabilities.NativeAssemblyProperties>
                {
                    new CLRCapabilities.NativeAssemblyProperties("mscorlib", 0x1549C856, new Version(100, 5, 0, 0)),
                };

                CompatibilityCheckResult result = DeploymentCompatibility.Check(
                    new[] { path },
                    device,
                    DeploymentCompatibility.GetDevicePeFormat(new Version(1, 18, 4, 10)));

                Assert.IsTrue(result.IsCompatible, result.FormatMessage());

                // same bundle on V2 firmware
                result = DeploymentCompatibility.Check(
                    new[] { path },
                    device,
                    DeploymentCompatibility.GetDevicePeFormat(new Version(2, 0, 1, 0)));

                Assert.AreEqual(2, result.Issues.Count(i => i.Kind == CompatibilityIssueKind.PeFormatMismatch));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [TestMethod]
        public void V1FirmwareRejectsV2Pes()
        {
            // nanoCLR 1.x: V1 firmware can't load NFMRK2 records
            string path = PeFileReaderTests.Asset(@"V2\UnitTestLauncher.bin");

            var device = new List<CLRCapabilities.NativeAssemblyProperties>
            {
                new CLRCapabilities.NativeAssemblyProperties("mscorlib", 0x2D5CA905, new Version(1, 0, 0, 0)),
            };

            CompatibilityCheckResult result = DeploymentCompatibility.Check(
                new[] { path },
                device,
                DeploymentCompatibility.GetDevicePeFormat(new Version(1, 18, 4, 10)));

            Assert.AreEqual(3, result.Issues.Count);
            Assert.IsTrue(result.Issues.All(i => i.Kind == CompatibilityIssueKind.PeFormatMismatch && i.RequiredFormat == PeFormat.V1));

            string message = result.FormatMessage();
            StringAssert.Contains(message, "is V2 (NFMRK2), V1 (NFMRK1) is required");
            StringAssert.Contains(message, "only loads V1 (NFMRK1) PE files");
        }

        [TestMethod]
        public void V2FirmwareRejectsV1Pes()
        {
            // nanoCLR 2.x: V2 firmware
            string path = PeFileReaderTests.Asset(@"V1\nanoFramework.ResourceManager.pe");

            var device = new List<CLRCapabilities.NativeAssemblyProperties>
            {
                new CLRCapabilities.NativeAssemblyProperties("nanoFramework.ResourceManager", 0xDCD7DF4D, new Version(1, 0, 0, 0)),
            };

            CompatibilityCheckResult result = DeploymentCompatibility.Check(
                new[] { path },
                device,
                DeploymentCompatibility.GetDevicePeFormat(new Version(2, 0, 1, 0)));

            CompatibilityIssue issue = result.Issues.Single();
            Assert.AreEqual(CompatibilityIssueKind.PeFormatMismatch, issue.Kind);
            Assert.AreEqual(path, issue.SourcePath);

            string message = result.FormatMessage();
            StringAssert.Contains(message, "wrong format");
            StringAssert.Contains(message, "'nanoFramework.ResourceManager' v1.2.32.0 is V1 (NFMRK1), V2 (NFMRK2) is required");
            StringAssert.Contains(message, path);
            StringAssert.Contains(message, "rebuild with the v2 toolchain");
        }

        [TestMethod]
        public void RealMixedFormatDeploymentImage()
        {
            // real image built with stale V1 class libraries and a V2 application
            string path = PeFileReaderTests.Asset("Mixed_NFApp1.bin");

            IReadOnlyList<PeAssemblyInfo> assemblies = PeFileReader.ReadFile(path);
            CollectionAssert.AreEqual(
                new[] { PeFormat.V1, PeFormat.V1, PeFormat.V1, PeFormat.V1, PeFormat.V2 },
                assemblies.Select(a => a.Format).ToArray());

            // device native assemblies matching the V1 hashes: only the format is wrong
            var device = assemblies
                .Where(a => a.NativeHash != 0)
                .Select(a => new CLRCapabilities.NativeAssemblyProperties(a.Name, a.NativeHash, new Version(1, 0, 0, 0)))
                .ToList();

            CompatibilityCheckResult result = DeploymentCompatibility.Check(new[] { path }, device, PeFormat.V2);

            Assert.AreEqual(4, result.Issues.Count);
            Assert.IsTrue(result.Issues.All(i => i.Kind == CompatibilityIssueKind.PeFormatMismatch));
            CollectionAssert.AreEquivalent(
                new[] { "mscorlib", "nanoFramework.Runtime.Events", "nanoFramework.Runtime.Native", "System.Device.Gpio" },
                result.Issues.Select(i => i.Assembly.Name).ToArray());

            // assemblies inside the image are located by their offset
            StringAssert.Contains(result.FormatMessage(), $"{path} @ 0x{assemblies[1].Offset:X}");

            // without an expected format, the mix is still reported
            result = DeploymentCompatibility.Check(assemblies, NativeAssemblyDescriptor.FromDevice(device));
            Assert.AreEqual(4, result.Issues.Count(i => i.Kind == CompatibilityIssueKind.PeFormatMismatch));
        }

        private static IReadOnlyList<PeAssemblyInfo> Read(params PeBuilder[] assemblies) =>
            PeFileReader.Read(PeBuilder.BuildImage(assemblies));

        private static string WriteTempFile(byte[] data)
        {
            string path = Path.Combine(Path.GetTempPath(), $"nf-compat-{Guid.NewGuid():N}.bin");
            File.WriteAllBytes(path, data);
            return path;
        }
    }
}
