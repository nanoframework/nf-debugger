// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using nanoFramework.Tools.Debugger.PortTcpIp;

namespace nanoFramework.Tools.Debugger.Tests
{
    /// <summary>
    /// A device on this machine announces itself from each of the adapters it sends to: one device, not one per adapter.
    /// 127.0.0.1 and 127.0.0.2 stand for two adapters of this machine, 10.0.0.x for other machines.
    /// </summary>
    [TestClass]
    public class NetworkLocalAnnouncementsTests
    {
        private static readonly IPAddress Adapter1 = IPAddress.Parse("127.0.0.1");
        private static readonly IPAddress Adapter2 = IPAddress.Parse("127.0.0.2");

        private static void Announce(PortTcpIpManager manager, int port, IPAddress sender) =>
            manager.Watcher.ProcessDiscoveryMessage($"+:{port}:TEST", sender, CancellationToken.None);

        private static void Depart(PortTcpIpManager manager, int port, IPAddress sender) =>
            manager.Watcher.ProcessDiscoveryMessage($"-:{port}", sender, CancellationToken.None);

        private static List<string> Listed(int port) =>
            NanoDeviceCollection.Instance
                .OfType<NanoDevice<NanoNetworkDevice>>()
                .Select(d => d.DeviceId)
                .Where(id => id.EndsWith($":{port}", StringComparison.Ordinal))
                .ToList();

        private static bool WaitForListed(int port, int count, int timeoutMilliseconds = 8000) =>
            SpinWait.SpinUntil(() => Listed(port).Count == count, timeoutMilliseconds);

        [TestMethod]
        public void TwoLocalSenders_OnOnePort_GiveOneDevice_TheFirstWins()
        {
            using var fake = new FakeNetworkDevice("secret");
            using var manager = new PortTcpIpManager(false, enableDiscovery: false);

            Announce(manager, fake.Port, Adapter1);
            Assert.IsTrue(WaitForListed(fake.Port, 1));

            Announce(manager, fake.Port, Adapter2);
            Thread.Sleep(1500);

            var listed = Listed(fake.Port);
            Assert.AreEqual(1, listed.Count);
            Assert.AreEqual($"tcpip://127.0.0.1:{fake.Port}", listed[0], "the first address is kept, as announced");
        }

        [TestMethod]
        public void TwoLocalSenders_AtTheSameTime_GiveOneDevice()
        {
            for (var i = 0; i < 4; i++)
            {
                using var fake = new FakeNetworkDevice("secret");
                using var manager = new PortTcpIpManager(false, enableDiscovery: false);
                using var start = new Barrier(2);

                var senders = new[] { Adapter1, Adapter2 }
                    .Select(address => Task.Run(() =>
                    {
                        start.SignalAndWait();

                        Announce(manager, fake.Port, address);
                    }))
                    .ToArray();

                Task.WaitAll(senders);

                Assert.IsTrue(WaitForListed(fake.Port, 1), $"round {i}");
                Thread.Sleep(1000);
                Assert.AreEqual(1, Listed(fake.Port).Count, $"round {i}");
            }
        }

        [TestMethod]
        public void ManyAnnouncementsAtTheSameTime_RaiseAddedOnce()
        {
            using var manager = new PortTcpIpManager(false, enableDiscovery: false);
            using var watcher = new DeviceWatcher(manager, 0);

            var added = 0;
            watcher.Added += (s, info) =>
            {
                Interlocked.Increment(ref added);

                // slow, so the other announcements arrive while this one is being added
                Thread.Sleep(300);
            };

            using var start = new Barrier(16);

            Task.WaitAll(Enumerable.Range(0, 16)
                .Select(i => Task.Run(() =>
                {
                    start.SignalAndWait();

                    watcher.ProcessDiscoveryMessage("+:26000:TEST", i % 2 == 0 ? Adapter1 : Adapter2, CancellationToken.None);
                }))
                .ToArray());

            SpinWait.SpinUntil(() => false, 800);

            Assert.AreEqual(1, added);
        }

        [TestMethod]
        public void TwoLocalPorts_GiveTwoDevices()
        {
            using var first = new FakeNetworkDevice("secret");
            using var second = new FakeNetworkDevice("secret");
            using var manager = new PortTcpIpManager(false, enableDiscovery: false);

            Announce(manager, first.Port, Adapter1);
            Announce(manager, second.Port, Adapter2);

            Assert.IsTrue(WaitForListed(first.Port, 1));
            Assert.IsTrue(WaitForListed(second.Port, 1));

            // each one announced from the other adapter too
            Announce(manager, first.Port, Adapter2);
            Announce(manager, second.Port, Adapter1);
            Thread.Sleep(1500);

            Assert.AreEqual(1, Listed(first.Port).Count);
            Assert.AreEqual(1, Listed(second.Port).Count);
        }

        [TestMethod]
        public void Departure_FromTheOtherLocalAddress_RemovesTheDevice()
        {
            using var fake = new FakeNetworkDevice("secret");
            using var manager = new PortTcpIpManager(false, enableDiscovery: false);

            Announce(manager, fake.Port, Adapter1);
            Assert.IsTrue(WaitForListed(fake.Port, 1));

            // the device was listed under the first address, it leaves announcing from the second
            Depart(manager, fake.Port, Adapter2);

            Assert.IsTrue(WaitForListed(fake.Port, 0, 5000), "the device is removed whichever address announces the departure");
        }

        [TestMethod]
        public void Departure_FromTheSameAddress_RemovesTheDevice_AndItCanArriveAgain()
        {
            using var fake = new FakeNetworkDevice("secret");
            using var manager = new PortTcpIpManager(false, enableDiscovery: false);

            Announce(manager, fake.Port, Adapter1);
            Assert.IsTrue(WaitForListed(fake.Port, 1));

            Depart(manager, fake.Port, Adapter1);
            Assert.IsTrue(WaitForListed(fake.Port, 0, 5000));

            // and when it comes back from the other adapter, that's the address it gets
            Announce(manager, fake.Port, Adapter2);
            Assert.IsTrue(WaitForListed(fake.Port, 1));
            Assert.AreEqual($"tcpip://127.0.0.2:{fake.Port}", Listed(fake.Port).Single());
        }

        [TestMethod]
        public void RemoteSenders_AreNotMerged()
        {
            using var manager = new PortTcpIpManager(false, enableDiscovery: false);
            using var watcher = new DeviceWatcher(manager, 0)
            {
                // only the loopback addresses are this machine
                IsLocalAddress = address => IPAddress.IsLoopback(address)
            };

            var added = new List<string>();
            var removed = new List<string>();
            watcher.Added += (s, info) => { lock (added) { added.Add(info.DeviceId); } };
            watcher.Removed += (s, info) => { lock (removed) { removed.Add(info.DeviceId); } };

            // one machine with two interfaces: listed twice, as before
            watcher.ProcessDiscoveryMessage("+:26000:TEST", IPAddress.Parse("10.0.0.1"), CancellationToken.None);
            watcher.ProcessDiscoveryMessage("+:26000:TEST", IPAddress.Parse("10.0.0.2"), CancellationToken.None);

            Assert.IsTrue(SpinWait.SpinUntil(() => { lock (added) { return added.Count == 2; } }, 5000));
            CollectionAssert.AreEquivalent(new[] { "tcpip://10.0.0.1:26000", "tcpip://10.0.0.2:26000" }, added);

            // and each leaves from its own address
            watcher.ProcessDiscoveryMessage("-:26000", IPAddress.Parse("10.0.0.2"), CancellationToken.None);

            CollectionAssert.AreEqual(new[] { "tcpip://10.0.0.2:26000" }, removed);
        }

        [TestMethod]
        public void WhenTheListedAddressIsGone_TheDeviceMovesToTheAddressItAnnouncesFrom()
        {
            using var fake = new FakeNetworkDevice("secret");
            using var manager = new PortTcpIpManager(false, enableDiscovery: false);

            Announce(manager, fake.Port, Adapter1);
            Assert.IsTrue(WaitForListed(fake.Port, 1));
            Assert.AreEqual($"tcpip://127.0.0.1:{fake.Port}", Listed(fake.Port).Single());

            // the adapter of the first address went away; the device still announces from the other one
            manager.Watcher.IsLocalAddress = address => address.Equals(Adapter2);

            Announce(manager, fake.Port, Adapter2);

            Assert.IsTrue(
                SpinWait.SpinUntil(() => Listed(fake.Port).SequenceEqual(new[] { $"tcpip://127.0.0.2:{fake.Port}" }), 8000),
                "the device must not stay on the address that is gone");
        }

        [TestMethod]
        public void LocalAddresses_RecognizesLoopback_AndNotOtherMachines()
        {
            Assert.IsTrue(LocalAddresses.IsLocal(IPAddress.Loopback));
            Assert.IsTrue(LocalAddresses.IsLocal(IPAddress.Parse("127.0.0.2")));
            Assert.IsTrue(LocalAddresses.IsLocal(IPAddress.IPv6Loopback));
            Assert.IsTrue(LocalAddresses.IsLocal(IPAddress.Parse("127.0.0.1").MapToIPv6()));

            Assert.IsFalse(LocalAddresses.IsLocal(null));
            Assert.IsFalse(LocalAddresses.IsLocal(IPAddress.Parse("203.0.113.77")), "documentation range, never an address of this machine");
        }
    }
}
