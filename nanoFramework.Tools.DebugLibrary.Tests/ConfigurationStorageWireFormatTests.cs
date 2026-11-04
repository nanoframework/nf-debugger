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

        [TestMethod]
        public void WPAuthTokenOptionValue()
        {
            Assert.AreEqual((byte)6, (byte)DeviceConfiguration.DeviceConfigurationOption.WPAuthToken);
        }

        [TestMethod]
        public void WPAuthTokenBlockLayout()
        {
            byte[] serialized = new Converter().Serialize(WPAuthTokenConfigurationBase.Create("s3cret"));

            // marker (4) + token length (1) + token (64): packed, same as HAL_Configuration_WPAuthToken
            Assert.AreEqual(4 + 1 + 64, serialized.Length);

            CollectionAssert.AreEqual(new byte[] { (byte)'A', (byte)'T', (byte)'1', 0 }, serialized[0..4]);
            Assert.AreEqual(6, serialized[4]);
            CollectionAssert.AreEqual(System.Text.Encoding.UTF8.GetBytes("s3cret"), serialized[5..11]);

            // the rest of the token is zero padded
            foreach (byte b in serialized[11..])
            {
                Assert.AreEqual(0, b);
            }
        }

        [TestMethod]
        public void WPAuthTokenBlockEmptyTokenTurnsAuthenticationOff()
        {
            byte[] serialized = new Converter().Serialize(WPAuthTokenConfigurationBase.Create(string.Empty));

            Assert.AreEqual(69, serialized.Length);
            Assert.AreEqual(0, serialized[4]);
        }

        [TestMethod]
        public void WPAuthTokenBlockTokenOfMaxLength()
        {
            string token = new string('x', 64);

            byte[] serialized = new Converter().Serialize(WPAuthTokenConfigurationBase.Create(token));

            Assert.AreEqual(69, serialized.Length);
            Assert.AreEqual(64, serialized[4]);
            Assert.AreEqual((byte)'x', serialized[68]);
        }

        [TestMethod]
        public void WPAuthTokenBlockLengthIsInBytesNotCharacters()
        {
            // 32 characters of 2 bytes each fit, 33 do not
            byte[] serialized = new Converter().Serialize(WPAuthTokenConfigurationBase.Create(new string('é', 32)));
            Assert.AreEqual(64, serialized[4]);

            Assert.ThrowsException<ArgumentException>(() => WPAuthTokenConfigurationBase.Create(new string('é', 33)));
        }

        [TestMethod]
        public void WPAuthTokenBlockTooLongTokenIsRejected()
        {
            Assert.ThrowsException<ArgumentException>(() => WPAuthTokenConfigurationBase.Create(new string('x', 65)));
        }

        [TestMethod]
        public void WPAuthTokenBlockNullTokenIsRejected()
        {
            Assert.ThrowsException<ArgumentNullException>(() => WPAuthTokenConfigurationBase.Create(null));
        }

        [TestMethod]
        public void WPAuthTokenOperationCommandLayout()
        {
            byte[] block = new Converter().Serialize(WPAuthTokenConfigurationBase.Create("abc"));

            var cmd = new Commands.Monitor_ConfigurationOperation
            {
                Operation = (uint)ConfigurationOperation.Replace,
                Configuration = (uint)DeviceConfiguration.DeviceConfigurationOption.WPAuthToken,
                BlockIndex = 0,
                Flags = (uint)(ConfigurationOperationFlags.First | ConfigurationOperationFlags.Last | ConfigurationOperationFlags.Upsert),
                TotalLength = (uint)block.Length,
            };

            // the block is small: it goes in a single chunk
            cmd.PrepareForSend(block, block.Length);

            byte[] serialized = new Converter().Serialize(cmd);

            Assert.AreEqual((7 * 4) + 69, serialized.Length);

            using var reader = new BinaryReader(new MemoryStream(serialized));

            Assert.AreEqual((uint)ConfigurationOperation.Replace, reader.ReadUInt32());
            Assert.AreEqual(6u, reader.ReadUInt32());
            Assert.AreEqual(0u, reader.ReadUInt32());
            reader.ReadUInt32();
            Assert.AreEqual(69u, reader.ReadUInt32());
            Assert.AreEqual(0u, reader.ReadUInt32());
            Assert.AreEqual(69u, reader.ReadUInt32());
            CollectionAssert.AreEqual(block, reader.ReadBytes(69));
        }

        [TestMethod]
        public void InfoReplyWithSixBlockTypes()
        {
            byte[] data = Build(
                w =>
                {
                    // version, flags, total size, free size, count of block types
                    w.Write(1u);
                    w.Write(0u);
                    w.Write(0x8000u);
                    w.Write(0x4000u);
                    w.Write(6u);

                    // all the types of the firmware with the network channel: 1 to 5 and the Wire Protocol one
                    for (byte option = 1; option <= 6; option++)
                    {
                        bool isWPAuthToken = option == (byte)DeviceConfiguration.DeviceConfigurationOption.WPAuthToken;

                        w.Write(option);
                        w.Write((byte)(isWPAuthToken ? 1 : 0));
                        w.Write((byte)(isWPAuthToken ? 1 : 4));
                        w.Write((byte)(ConfigurationBlockFlags.CanAdd | ConfigurationBlockFlags.CanReplace | ConfigurationBlockFlags.CanDelete));
                        w.Write(isWPAuthToken ? 69u : 100u);
                    }
                });

            var reply = new Commands.Monitor_ConfigurationInfo.Reply();
            var converter = new Converter();
            reply.PrepareForDeserialize(data.Length, data, converter);
            converter.Deserialize(reply, data);

            var info = new ConfigurationStorageInfo(reply);

            Assert.AreEqual(6, info.Blocks.Count);

            ConfigurationBlockInfo token = info[DeviceConfiguration.DeviceConfigurationOption.WPAuthToken];
            Assert.IsNotNull(token);
            Assert.AreEqual(1, token.MaxCount);
            Assert.AreEqual(69u, token.MaxBlockSize);
            Assert.IsTrue(token.IsStored);
            Assert.IsFalse(token.Flags.HasFlag(ConfigurationBlockFlags.Readable));

            Assert.IsNotNull(info[DeviceConfiguration.DeviceConfigurationOption.X509DeviceCertificates]);
        }

        [TestMethod]
        public void InfoReplyWithoutWPAuthTokenBlockType()
        {
            // firmware without the network channel: the type is not reported
            byte[] data = Build(
                w =>
                {
                    w.Write(1u);
                    w.Write(0u);
                    w.Write(0x8000u);
                    w.Write(0x4000u);
                    w.Write(1u);

                    w.Write((byte)DeviceConfiguration.DeviceConfigurationOption.Network);
                    w.Write((byte)1);
                    w.Write((byte)1);
                    w.Write((byte)ConfigurationBlockFlags.CanReplace);
                    w.Write(100u);
                });

            var reply = new Commands.Monitor_ConfigurationInfo.Reply();
            var converter = new Converter();
            reply.PrepareForDeserialize(data.Length, data, converter);
            converter.Deserialize(reply, data);

            Assert.IsNull(new ConfigurationStorageInfo(reply)[DeviceConfiguration.DeviceConfigurationOption.WPAuthToken]);
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
