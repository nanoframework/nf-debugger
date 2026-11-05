// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.Debugger.PortComposite;
using nanoFramework.Tools.Debugger.PortTcpIp;
using nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.Debugger.Tests
{
    /// <summary>
    /// A device on the network that talks just enough Wire Protocol: it answers the ping, and the authentication.
    /// </summary>
    internal sealed class FakeNetworkDevice : IDisposable
    {
        private const int HeaderSize = 32;

        // any address of this machine: the tests announce the device from more than one
        private readonly TcpListener _listener = new TcpListener(IPAddress.Any, 0);
        private readonly List<TcpClient> _clients = new List<TcpClient>();
        private readonly int _dropAfterFailures;
        private int _authenticateAttempts;

        /// <param name="expectedToken">The token that is accepted.</param>
        /// <param name="dropAfterFailures">Failed attempts after which the connection is dropped, 3 in the real device.</param>
        public FakeNetworkDevice(string expectedToken, int dropAfterFailures = 3)
        {
            ExpectedToken = expectedToken;
            _dropAfterFailures = dropAfterFailures;

            _listener.Start();

            new Thread(AcceptLoop) { IsBackground = true }.Start();
        }

        public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

        public string ExpectedToken { get; }

        public int AuthenticateAttempts => Volatile.Read(ref _authenticateAttempts);

        private void AcceptLoop()
        {
            try
            {
                while (true)
                {
                    var client = _listener.AcceptTcpClient();

                    lock (_clients)
                    {
                        _clients.Add(client);
                    }

                    new Thread(() => Serve(client)) { IsBackground = true }.Start();
                }
            }
            catch
            {
                // the listener was stopped
            }
        }

        private void Serve(TcpClient client)
        {
            var converter = new Converter();
            var authenticated = false;

            try
            {
                var stream = client.GetStream();

                while (true)
                {
                    var header = ReadExactly(stream, HeaderSize);
                    var command = BitConverter.ToUInt32(header, 16);
                    var seq = BitConverter.ToUInt16(header, 20);
                    var size = (int)BitConverter.ToUInt32(header, 28);
                    var payload = size > 0 ? ReadExactly(stream, size) : new byte[0];

                    object reply = null;
                    uint flags = Flags.c_Reply;
                    var dropAfterReply = false;

                    if (command == Commands.c_Monitor_Ping)
                    {
                        reply = new Commands.Monitor_Ping.Reply
                        {
                            Source = Commands.Monitor_Ping.c_Ping_Source_NanoCLR,
                            Flags = Commands.Monitor_Ping.Monitor_Ping_c_PacketSize_1024
                        };
                    }
                    else if (command == Commands.c_Monitor_Authenticate)
                    {
                        var length = BitConverter.ToUInt32(payload, 0);
                        var token = Encoding.UTF8.GetString(payload, 4, (int)length);
                        var attempt = Interlocked.Increment(ref _authenticateAttempts);

                        authenticated = token == ExpectedToken;

                        // 2: wrong token, and the connection is dropped (too many failures)
                        dropAfterReply = !authenticated && attempt >= _dropAfterFailures;
                        reply = new Commands.Monitor_Authenticate.Reply { Result = authenticated ? 0u : dropAfterReply ? 2u : 1u };
                    }
                    else if (!authenticated)
                    {
                        flags |= Flags.c_NACK;
                    }
                    else
                    {
                        // nothing else is implemented
                        continue;
                    }

                    var body = reply is null ? null : converter.Serialize(reply);

                    var packet = new Packet
                    {
                        Cmd = command,
                        Seq = 1,
                        SeqReply = seq,
                        Flags = flags,
                        Size = (uint)(body?.Length ?? 0),
                        CrcData = body is null ? 0 : CRC.ComputeCRC(body, 0)
                    };

                    var bytes = converter.Serialize(packet);
                    stream.Write(bytes, 0, bytes.Length);

                    if (body is not null)
                    {
                        stream.Write(body, 0, body.Length);
                    }

                    if (dropAfterReply)
                    {
                        client.Close();

                        return;
                    }
                }
            }
            catch
            {
                // the client went away
            }
        }

        private static byte[] ReadExactly(Stream stream, int count)
        {
            var buffer = new byte[count];
            var read = 0;

            while (read < count)
            {
                var n = stream.Read(buffer, read, count - read);

                if (n <= 0)
                {
                    throw new EndOfStreamException();
                }

                read += n;
            }

            return buffer;
        }

        public void Dispose()
        {
            _listener.Stop();

            lock (_clients)
            {
                _clients.ForEach(c => c.Close());
            }
        }
    }

    [TestClass]
    public class NetworkAuthenticationTests
    {
        private static int FreeUdpPort()
        {
            using var udp = new UdpClient(0);

            return ((IPEndPoint)udp.Client.LocalEndPoint).Port;
        }

        private static void Announce(int discoveryPort, int devicePort, string targetName)
        {
            using var udp = new UdpClient();
            var data = Encoding.UTF8.GetBytes($"+:{devicePort}:{targetName}");
            udp.Send(data, data.Length, new IPEndPoint(IPAddress.Loopback, discoveryPort));
        }

        /// <summary>
        /// Starts a manager that listens on its own discovery port, and has the device announce itself.
        /// </summary>
        private static NanoDeviceBase Discover(PortTcpIpManager manager, int discoveryPort, FakeNetworkDevice fake)
        {
            manager.StartDeviceWatchers();

            // the listener takes a moment to be ready, and a datagram sent before is lost: announce until it's picked up
            NanoDeviceBase device = null;
            // (slowly: the arrival takes a while, and each announcement starts one more)
            for (var i = 0; i < 6 && device is null; i++)
            {
                Announce(discoveryPort, fake.Port, "ESP32_TEST");

                device = Find($"tcpip://127.0.0.1:{fake.Port}", i == 0 ? 1000 : 2500);
            }

            // the arrival still holds the device for a moment after listing it; a retry in the meantime does nothing
            SpinWait.SpinUntil(() => false, 1000);

            return device;
        }

        private static NanoDeviceBase Find(string deviceId, int timeoutMilliseconds)
        {
            NanoDeviceBase found = null;

            SpinWait.SpinUntil(
                () =>
                {
                    // enumeration works on a snapshot
                    found = NanoDeviceCollection.Instance.FirstOrDefault(d => (d as NanoDevice<NanoNetworkDevice>)?.DeviceId == deviceId);

                    return found is not null;
                },
                timeoutMilliseconds);

            return found;
        }

        [TestMethod]
        public void Device_WithoutToken_IsListedAsTokenMissing_AndGatedCommandsFail()
        {
            using var fake = new FakeNetworkDevice("secret");
            var discoveryPort = FreeUdpPort();
            using var manager = new PortTcpIpManager(false, discoveryPort);

            var device = Discover(manager, discoveryPort, fake);

            Assert.IsNotNull(device, "answers the ping, so it's a nanoFramework device");
            Assert.AreEqual(DeviceAuthenticationState.TokenMissing, device.AuthenticationState);
            Assert.AreSame(device, Find($"tcpip://127.0.0.1:{fake.Port}", 100));
            Assert.AreEqual(1, fake.AuthenticateAttempts);

            Assert.ThrowsExactly<DeviceAuthenticationFailedException>(() => device.DebugEngine.GetMonitorTargetInfo());
            Assert.ThrowsExactly<DeviceAuthenticationFailedException>(() => device.GetDeviceInfo());

            // a connection attempt doesn't send the token that is known to fail
            Assert.ThrowsExactly<DeviceAuthenticationFailedException>(() => device.DebugEngine.Connect());
            Assert.AreEqual(1, fake.AuthenticateAttempts);
        }

        [TestMethod]
        public void RetryAuthentication_DoesNotResendARejectedToken_AndSucceedsWithTheRightOne()
        {
            using var fake = new FakeNetworkDevice("secret", dropAfterFailures: int.MaxValue);
            var discoveryPort = FreeUdpPort();
            using var manager = new PortTcpIpManager(false, discoveryPort);

            string token = null;
            manager.TokenProvider = _ => token;

            var device = Discover(manager, discoveryPort, fake);
            var changes = new List<DeviceAuthenticationState>();
            device.AuthenticationStateChanged += (s, e) => changes.Add(device.AuthenticationState);

            Assert.AreEqual(DeviceAuthenticationState.TokenMissing, device.AuthenticationState);

            // still no token: nothing is sent
            Assert.AreEqual(DeviceAuthenticationState.TokenMissing, device.RetryAuthentication());
            Assert.AreEqual(1, fake.AuthenticateAttempts);

            token = "wrong";
            Assert.AreEqual(DeviceAuthenticationState.TokenRejected, device.RetryAuthentication());
            Assert.AreEqual(2, fake.AuthenticateAttempts);

            // same token again: not sent
            Assert.AreEqual(DeviceAuthenticationState.TokenRejected, device.RetryAuthentication());
            Assert.AreEqual(2, fake.AuthenticateAttempts);

            // unless forced
            Assert.AreEqual(DeviceAuthenticationState.TokenRejected, device.RetryAuthentication(force: true));
            Assert.AreEqual(3, fake.AuthenticateAttempts);

            token = "secret";
            Assert.AreEqual(DeviceAuthenticationState.Authenticated, device.RetryAuthentication());
            Assert.AreEqual(4, fake.AuthenticateAttempts);

            CollectionAssert.AreEqual(
                new[] { DeviceAuthenticationState.TokenRejected, DeviceAuthenticationState.Authenticated },
                changes);

            // the device was kept in the list all along
            Assert.AreSame(device, Find($"tcpip://127.0.0.1:{fake.Port}", 100));
        }

        [TestMethod]
        public void RetryAuthentication_BackToBackRetries_EachSendsOneAttempt()
        {
            using var fake = new FakeNetworkDevice("secret", dropAfterFailures: int.MaxValue);
            var discoveryPort = FreeUdpPort();
            using var manager = new PortTcpIpManager(false, discoveryPort);

            string token = "wrong";
            manager.TokenProvider = _ => token;

            var device = Discover(manager, discoveryPort, fake);
            var expected = fake.AuthenticateAttempts;

            // no pauses: the connection released by an attempt must not interfere with the next one
            for (var i = 0; i < 15; i++)
            {
                Assert.AreEqual(DeviceAuthenticationState.TokenRejected, device.RetryAuthentication(force: true), $"retry {i}");
                Assert.AreEqual(++expected, fake.AuthenticateAttempts, $"attempts after retry {i}");
            }

            token = "secret";
            Assert.AreEqual(DeviceAuthenticationState.Authenticated, device.RetryAuthentication());
        }

        [TestMethod]
        public void RetryAuthentication_RightAfterTheDeviceDropped_NoPauses()
        {
            using var fake = new FakeNetworkDevice("secret");
            var discoveryPort = FreeUdpPort();
            using var manager = new PortTcpIpManager(false, discoveryPort);

            string token = null;
            manager.TokenProvider = _ => token;

            var device = Discover(manager, discoveryPort, fake);

            // arrival was the 1st failure, with no token
            token = "wrong";
            Assert.AreEqual(DeviceAuthenticationState.TokenRejected, device.RetryAuthentication());
            token = "wrong too";
            Assert.AreEqual(DeviceAuthenticationState.TokenRejected, device.RetryAuthentication());
            Assert.AreEqual(3, fake.AuthenticateAttempts);

            // the device has dropped the connection: the very next retry
            token = "secret";
            Assert.AreEqual(DeviceAuthenticationState.Authenticated, device.RetryAuthentication());
        }

        [TestMethod]
        public void RetryAuthentication_AfterTheDeviceDroppedTheConnection_Works()
        {
            using var fake = new FakeNetworkDevice("secret");
            var discoveryPort = FreeUdpPort();
            using var manager = new PortTcpIpManager(false, discoveryPort);

            string token = null;
            manager.TokenProvider = _ => token;

            var device = Discover(manager, discoveryPort, fake);

            token = "wrong";
            Assert.AreEqual(DeviceAuthenticationState.TokenRejected, device.RetryAuthentication());

            // the third failure: the device drops the connection but stays listed
            // let the engine settle after releasing the connection
            Thread.Sleep(1000);

            token = "wrong too";
            Assert.AreEqual(DeviceAuthenticationState.TokenRejected, device.RetryAuthentication());
            Assert.AreEqual(3, fake.AuthenticateAttempts);
            Assert.IsNotNull(Find($"tcpip://127.0.0.1:{fake.Port}", 100));

            // the engine takes a moment to settle after the drop
            Thread.Sleep(2500);

            token = "secret";
            Assert.AreEqual(DeviceAuthenticationState.Authenticated, device.RetryAuthentication());
        }

        [TestMethod]
        public void DiscoveredDevice_NeedingAToken_ArrivesWithTheAnnouncedName()
        {
            using var fake = new FakeNetworkDevice("secret");
            var discoveryPort = FreeUdpPort();
            using var manager = new PortTcpIpManager(false, discoveryPort);

            var device = Discover(manager, discoveryPort, fake);

            Assert.IsNotNull(device, "a device that needs a token must still arrive");
            Assert.AreEqual("ESP32_TEST", device.TargetName);
            Assert.AreEqual(DeviceAuthenticationState.TokenMissing, device.AuthenticationState);
        }

        [TestMethod]
        public void ReScan_AuthenticatesAListedDevice_WhenTheTokenIsAvailableNow()
        {
            using var fake = new FakeNetworkDevice("secret");
            var discoveryPort = FreeUdpPort();
            using var manager = new PortTcpIpManager(false, discoveryPort);

            string token = null;
            manager.TokenProvider = _ => token;

            var device = Discover(manager, discoveryPort, fake);
            Assert.AreEqual(DeviceAuthenticationState.TokenMissing, device.AuthenticationState);

            token = "secret";
            manager.ReScanDevices();

            // the check also reads the identity of the device, which the fake doesn't answer: that takes a while to give up
            Assert.IsTrue(SpinWait.SpinUntil(() => device.AuthenticationState == DeviceAuthenticationState.Authenticated, 20000));
            Assert.AreSame(device, Find($"tcpip://127.0.0.1:{fake.Port}", 1000), "it's reachable, so it stays listed");
        }

        [TestMethod]
        public void DiscoveryDisabled_DoesNotListen_EvenIfTheWatchersAreStarted()
        {
            using var fake = new FakeNetworkDevice("secret");
            var discoveryPort = FreeUdpPort();
            using var manager = new PortTcpIpManager(false, discoveryPort, enableDiscovery: false);

            Assert.IsFalse(manager.IsDiscoveryEnabled);

            using var composite = new PortCompositeDeviceManager(new PortBase[] { manager });
            composite.StartDeviceWatchers();

            SpinWait.SpinUntil(() => false, 300);
            Announce(discoveryPort, fake.Port, "ESP32_TEST");

            Assert.IsNull(Find($"tcpip://127.0.0.1:{fake.Port}", 1500), "nobody listens for the announcements");
        }

        [TestMethod]
        public void AddDevice_IsNotSupported_ForNetworkDevices()
        {
            using var manager = new PortTcpIpManager(false, enableDiscovery: false);

            Assert.ThrowsExactly<NotSupportedException>(() => manager.AddDevice("tcpip://127.0.0.1:26000"));
        }
    }
}
