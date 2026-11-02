//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.Debugger.Tests
{
    // the layouts have to match Monitor_ConfigurationInfo_Reply, Monitor_ConfigurationOperation_Command and
    // Monitor_ConfigurationOperation_Reply in WireProtocol_MonitorCommands.h (in nf-interpreter)
    [TestClass]
    public class ConfigurationStorageWireFormatTests
    {
        [TestMethod]
        public void OperationCommandLayout()
        {
            byte[] block = { 0x10, 0x11, 0x12, 0x13, 0x14 };

            var cmd = new Commands.Monitor_ConfigurationOperation
            {
                Operation = (uint)ConfigurationOperation.Replace,
                Configuration = (uint)DeviceConfiguration.DeviceConfigurationOption.Wireless80211Network,
                BlockIndex = 3,
                Flags = (uint)(ConfigurationOperationFlags.First | ConfigurationOperationFlags.KeepPassword),
                TotalLength = (uint)block.Length,
            };

            // 7 uint fields before the data
            Assert.AreEqual(7 * 4, cmd.Overhead);

            cmd.PrepareForSend(block, 3, 2);

            byte[] serialized = new Converter().Serialize(cmd);

            Assert.AreEqual(7 * 4 + 3, serialized.Length);

            using var reader = new BinaryReader(new MemoryStream(serialized));

            Assert.AreEqual(2u, reader.ReadUInt32());
            Assert.AreEqual(2u, reader.ReadUInt32());
            Assert.AreEqual(3u, reader.ReadUInt32());
            Assert.AreEqual(0x11u, reader.ReadUInt32());
            Assert.AreEqual(5u, reader.ReadUInt32());
            Assert.AreEqual(2u, reader.ReadUInt32());
            Assert.AreEqual(3u, reader.ReadUInt32());
            CollectionAssert.AreEqual(new byte[] { 0x12, 0x13, 0x14 }, reader.ReadBytes(3));
        }

        [TestMethod]
        public void OperationReply()
        {
            byte[] data = Build(
                w =>
                {
                    w.Write((uint)ConfigurationOperationError.Success);
                    w.Write(2u);
                    w.Write(3u);
                    w.Write(7u);
                });

            var reply = new Commands.Monitor_ConfigurationOperation.Reply();
            new Converter().Deserialize(reply, data);

            var result = new ConfigurationOperationResult(reply);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.BlockIndex);
            Assert.AreEqual(3, result.Count);
            Assert.AreEqual(7u, result.Id);
        }

        [TestMethod]
        public void OperationReplyWithoutIndexNorId()
        {
            byte[] data = Build(
                w =>
                {
                    w.Write((uint)ConfigurationOperationError.InUse);
                    w.Write(0xFFFFFFFFu);
                    w.Write(1u);
                    w.Write(0xFFFFFFFFu);
                });

            var reply = new Commands.Monitor_ConfigurationOperation.Reply();
            new Converter().Deserialize(reply, data);

            var result = new ConfigurationOperationResult(reply);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(ConfigurationOperationError.InUse, result.Error);
            Assert.IsNull(result.BlockIndex);
            Assert.IsNull(result.Id);
        }

        [TestMethod]
        public void InfoReply()
        {
            byte[] data = Build(
                w =>
                {
                    // version, flags, total size, free size (unknown), count of block types
                    w.Write(1u);
                    w.Write((uint)ConfigurationStorageFlags.ErasesStorage);
                    w.Write(0x8000u);
                    w.Write(0xFFFFFFFFu);
                    w.Write(2u);

                    // wireless profiles: 2 of 10, add/replace/delete/readable
                    w.Write((byte)DeviceConfiguration.DeviceConfigurationOption.Wireless80211Network);
                    w.Write((byte)2);
                    w.Write((byte)10);
                    w.Write((byte)(ConfigurationBlockFlags.CanAdd
                                   | ConfigurationBlockFlags.CanReplace
                                   | ConfigurationBlockFlags.CanDelete
                                   | ConfigurationBlockFlags.Readable));
                    w.Write(116u);

                    // CA root bundle: stored, limited by space
                    w.Write((byte)DeviceConfiguration.DeviceConfigurationOption.X509Certificate);
                    w.Write((byte)1);
                    w.Write((byte)0xFF);
                    w.Write((byte)(ConfigurationBlockFlags.CanAdd | ConfigurationBlockFlags.CanReplace | ConfigurationBlockFlags.CanDelete));
                    w.Write(4096u);
                });

            var reply = new Commands.Monitor_ConfigurationInfo.Reply();
            var converter = new Converter();
            reply.PrepareForDeserialize(data.Length, data, converter);
            converter.Deserialize(reply, data);

            var info = new ConfigurationStorageInfo(reply);

            Assert.AreEqual(1u, info.Version);
            Assert.AreEqual(ConfigurationStorageFlags.ErasesStorage, info.Flags);
            Assert.AreEqual(0x8000u, info.TotalSize);
            Assert.IsNull(info.FreeSize);
            Assert.AreEqual(2, info.Blocks.Count);

            ConfigurationBlockInfo wireless = info[DeviceConfiguration.DeviceConfigurationOption.Wireless80211Network];
            Assert.AreEqual(2, wireless.Count);
            Assert.AreEqual(10, wireless.MaxCount);
            Assert.IsTrue(wireless.CanAdd);
            Assert.IsTrue(wireless.Flags.HasFlag(ConfigurationBlockFlags.Readable));
            Assert.AreEqual(116u, wireless.MaxBlockSize);

            ConfigurationBlockInfo rootCa = info[DeviceConfiguration.DeviceConfigurationOption.X509Certificate];
            Assert.IsTrue(rootCa.IsStored);
            Assert.IsNull(rootCa.MaxCount);
            Assert.IsFalse(rootCa.Flags.HasFlag(ConfigurationBlockFlags.Readable));

            Assert.IsNull(info[DeviceConfiguration.DeviceConfigurationOption.WirelessNetworkAP]);
        }

        private static byte[] Build(Action<BinaryWriter> write)
        {
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream);

            write(writer);
            writer.Flush();

            return stream.ToArray();
        }
    }
}
