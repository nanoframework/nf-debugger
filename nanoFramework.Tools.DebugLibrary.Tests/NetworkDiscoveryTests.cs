// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.Debugger.PortTcpIp;
using nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.Debugger.Tests
{
    [TestClass]
    public class NetworkDiscoveryTests
    {
        [TestMethod]
        public void Announcement_IsParsed()
        {
            Assert.IsTrue(DiscoveryMessage.TryParse("+:26000:ESP32_REV3", out var message));

            Assert.IsTrue(message.IsAnnouncement);
            Assert.AreEqual(26000, message.Port);
            Assert.AreEqual("ESP32_REV3", message.TargetName);
        }

        [TestMethod]
        public void Announcement_TargetNameWithSeparator_IsKept()
        {
            Assert.IsTrue(DiscoveryMessage.TryParse("+:26000:A:B", out var message));

            Assert.AreEqual("A:B", message.TargetName);
        }

        [TestMethod]
        public void Departure_IsParsed()
        {
            Assert.IsTrue(DiscoveryMessage.TryParse("-:26000", out var message));

            Assert.IsFalse(message.IsAnnouncement);
            Assert.AreEqual(26000, message.Port);
        }

        [DataTestMethod]
        [DataRow("")]
        [DataRow("+")]
        [DataRow("+:26000")]
        [DataRow("+:abc:name")]
        [DataRow("+:0:name")]
        [DataRow("+:70000:name")]
        [DataRow("-")]
        [DataRow("-:")]
        [DataRow("-:abc")]
        [DataRow("-:26000:extra")]
        [DataRow("*:26000:name")]
        [DataRow("+:26000")] // no target name
        public void InvalidMessages_AreRejected(string text)
        {
            Assert.IsFalse(DiscoveryMessage.TryParse(text, out var message));
            Assert.IsNull(message);
        }

        [TestMethod]
        public void DeviceAddress_IsTheSenderOne()
        {
            Assert.IsTrue(DiscoveryMessage.TryParse("+:26000:ESP32_REV3", out var message));

            var info = message.ToDeviceInformation(IPAddress.Parse("192.168.1.50"));

            Assert.AreEqual("192.168.1.50", info.Host);
            Assert.AreEqual(26000, info.Port);
            Assert.AreEqual("ESP32_REV3", info.TargetName);
            Assert.AreEqual("tcpip://192.168.1.50:26000", info.DeviceId);
        }

        [TestMethod]
        public void DeviceAddress_MappedIPv4_IsIPv4()
        {
            Assert.IsTrue(DiscoveryMessage.TryParse("-:26000", out var message));

            var info = message.ToDeviceInformation(IPAddress.Parse("192.168.1.50").MapToIPv6());

            Assert.AreEqual("tcpip://192.168.1.50:26000", info.DeviceId);
        }

        [DataTestMethod]
        [DataRow("tcpip://192.168.1.50:26000", "192.168.1.50", 26000)]
        [DataRow("tcpip://my-device.local:26001", "my-device.local", 26001)]
        [DataRow("tcpip://MyDevice:1", "mydevice", 1)]
        [DataRow("192.168.1.50:26000", "192.168.1.50", 26000)]
        [DataRow(" tcpip://localhost:65535 ", "localhost", 65535)]
        public void DeviceId_IsParsed(string id, string host, int port)
        {
            Assert.IsTrue(NetworkDeviceInformation.TryParse(id, out var info));

            Assert.AreEqual(host, info.Host);
            Assert.AreEqual(port, info.Port);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("COM3")]
        [DataRow("tcpip://")]
        [DataRow("tcpip://host")]
        [DataRow("tcpip://host:")]
        [DataRow("tcpip://:26000")]
        [DataRow("tcpip://host:0")]
        [DataRow("tcpip://host:65536")]
        [DataRow("tcpip://host:-1")]
        [DataRow("tcpip://ho st:26000")]
        [DataRow("\"tcpip://1.2.3.4:26000\"gm")] // what the former regex tried to match
        [DataRow("tcpip://-host:26000")]
        public void InvalidDeviceId_IsRejected(string id)
        {
            Assert.IsFalse(NetworkDeviceInformation.TryParse(id, out _));
        }

        [TestMethod]
        public void ExclusiveAccessKey_IsTheDeviceId_ForEveryRoute()
        {
            // discovery builds the information from the datagram, a manual add parses the id: the key must be the same
            Assert.IsTrue(DiscoveryMessage.TryParse("+:26000:x", out var message));
            var fromDiscovery = message.ToDeviceInformation(IPAddress.Parse("10.0.0.7"));

            Assert.IsTrue(NetworkDeviceInformation.TryParse("tcpip://10.0.0.7:26000", out var fromId));
            var fromHostAndPort = new NetworkDeviceInformation("10.0.0.7", 26000);

            Assert.AreEqual(fromDiscovery.DeviceId, fromDiscovery.ExclusiveAccessKey);
            Assert.AreEqual(fromDiscovery.ExclusiveAccessKey, fromId.ExclusiveAccessKey);
            Assert.AreEqual(fromDiscovery.ExclusiveAccessKey, fromHostAndPort.ExclusiveAccessKey);
        }

        [TestMethod]
        public void ExclusiveAccess_ForNetworkAddress_IsSharedWithTheDeviceId()
        {
            var info = new NetworkDeviceInformation("10.99.99.99", 26000);

            using var first = NFDevice.GlobalExclusiveDeviceAccess.TryGet(info);
            Assert.IsNotNull(first);

            // the port locks by device id: it must be the same lock, so a second request in this context is a re-entrant one and not a different lock
            using var second = NFDevice.GlobalExclusiveDeviceAccess.TryGet(info.DeviceId, millisecondsTimeout: 200);
            Assert.IsNotNull(second);
            Assert.AreSame(first, second);
        }

        [TestMethod]
        public void AddDevice_InvalidId_Throws()
        {
            using var manager = new PortTcpIpManager(false);

            Assert.ThrowsExactly<ArgumentException>(() => manager.AddDevice("COM3"));
            Assert.ThrowsExactly<ArgumentException>(() => manager.AddDevice("\"tcpip://1.2.3.4:26000\"gm"));
            Assert.ThrowsExactly<ArgumentException>(() => manager.AddDevice("host with spaces", 26000));
            Assert.ThrowsExactly<ArgumentException>(() => manager.AddDevice("host", 0));
        }

        [TestMethod]
        public void AddDevice_NotReachable_ReturnsNull_AndRemoveDeviceIsIdempotent()
        {
            // a port where nothing listens
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            using var manager = new PortTcpIpManager(false);

            Assert.IsNull(manager.AddDevice("127.0.0.1", port));

            manager.RemoveDevice($"tcpip://127.0.0.1:{port}");
            manager.RemoveDevice($"tcpip://127.0.0.1:{port}");
        }

        [TestMethod]
        public void Authenticate_Constants_AreTheAgreedOnes()
        {
            var command = Commands.c_Monitor_Authenticate;
            var maxTokenLength = Commands.Monitor_Authenticate.MaxTokenLength;

            Assert.AreEqual(0x00000014u, command);
            Assert.AreEqual(64, maxTokenLength);
        }

        [TestMethod]
        public void AuthenticateRequest_IsLengthPrefixedUtf8()
        {
            var request = new Commands.Monitor_Authenticate("abcé");

            Assert.AreEqual(5u, request.Length);

            var bytes = new Converter().Serialize(request);

            CollectionAssert.AreEqual(
                new byte[] { 5, 0, 0, 0 }.Concat(Encoding.UTF8.GetBytes("abcé")),
                bytes);
        }

        [TestMethod]
        public void AuthenticateRequest_TokenTooLong_Throws()
        {
            Assert.ThrowsExactly<ArgumentException>(() => new Commands.Monitor_Authenticate(new string('a', 65)));

            // 64 bytes is fine
            Assert.AreEqual(64u, new Commands.Monitor_Authenticate(new string('a', 64)).Length);
        }

        [TestMethod]
        public void NanoNetworkDevice_ConnectsReadsAndReconnects()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();

            try
            {
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;

                using var device = new NanoNetworkDevice
                {
                    NetworkDeviceInformation = new NetworkDeviceInformation("127.0.0.1", port)
                };

                Assert.IsFalse(device.Connected);
                Assert.AreEqual(-1, device.AvailableBytes);

                var stream = device.Connect();
                using var server = listener.AcceptTcpClient();

                Assert.IsTrue(device.Connected);
                Assert.AreEqual(0, device.AvailableBytes);
                Assert.AreSame(stream, device.Connect(), "a second connect returns the current connection");

                server.GetStream().Write(new byte[] { 1, 2, 3 }, 0, 3);
                SpinWait.SpinUntil(() => device.AvailableBytes == 3, 2000);
                Assert.AreEqual(3, device.AvailableBytes);

                // the other side goes away
                server.GetStream().Write(new byte[] { 9 }, 0, 1);
                server.Close();
                var buffer = new byte[16];
                SpinWait.SpinUntil(() => device.AvailableBytes == 4, 2000);
                Assert.AreEqual(4, stream.Read(buffer, 0, 16));
                SpinWait.SpinUntil(() => device.AvailableBytes < 0, 2000);
                Assert.IsFalse(device.Connected, "closed by the other side");

                // close and dispose leave it ready to connect again, and don't throw when called repeatedly
                device.Close();
                device.Close();
                Assert.IsFalse(device.Connected);
                Assert.AreEqual(-1, device.AvailableBytes);

                device.Connect();
                using var server2 = listener.AcceptTcpClient();
                Assert.IsTrue(device.Connected);

                device.Dispose();
                Assert.IsFalse(device.Connected);
            }
            finally
            {
                listener.Stop();
            }
        }

        [TestMethod]
        public void NanoNetworkDevice_ConnectToNothing_Fails()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            using var device = new NanoNetworkDevice
            {
                NetworkDeviceInformation = new NetworkDeviceInformation("127.0.0.1", port)
            };

            Assert.ThrowsExactly<AggregateException>(() => device.Connect());
            Assert.IsFalse(device.Connected);
        }

        [TestMethod]
        public void NanoDevice_ForNetwork_HasTcpIpTransport()
        {
            var device = new NanoDevice<NanoNetworkDevice>();

            Assert.AreEqual(TransportType.TcpIp, device.Transport);
            Assert.IsFalse(device.SupportsNanoBooter);

            Assert.AreEqual(TransportType.Serial, new NanoDevice<NanoSerialDevice>().Transport);
        }

        [TestMethod]
        public void CreateDebugEngine_UsesTheTimeoutArgument()
        {
            var device = new NanoDevice<NanoNetworkDevice>();
            device.ConnectionPort = new PortTcpIp.PortTcpIp(
                new PortTcpIpManager(false),
                device,
                new NetworkDeviceInformation("127.0.0.1", 26000));
            device.DeviceId = "tcpip://127.0.0.1:26000";

            device.CreateDebugEngine(4321);

            Assert.AreEqual(4321, device.DebugEngine.DefaultTimeout);
        }

        [TestMethod]
        public void ConnectToNanoBooter_OverNetwork_IsNotSupported()
        {
            var device = new NanoDevice<NanoNetworkDevice>();

            // no engine is needed: it doesn't get that far
            Assert.IsFalse(device.ConnectToNanoBooter());
        }
    }

    internal static class ByteExtensions
    {
        public static byte[] Concat(this byte[] first, byte[] second)
        {
            var result = new byte[first.Length + second.Length];
            first.CopyTo(result, 0);
            second.CopyTo(result, first.Length);

            return result;
        }
    }
}
