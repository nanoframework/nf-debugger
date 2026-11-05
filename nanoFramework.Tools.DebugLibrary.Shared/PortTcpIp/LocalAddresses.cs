// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace nanoFramework.Tools.Debugger.PortTcpIp
{
    /// <summary>
    /// The addresses of this machine. They change while running (adapters come and go, VPNs are turned on and off),
    /// so the list is read again when it gets old.
    /// </summary>
    internal static class LocalAddresses
    {
        private static readonly object _lock = new object();
        private static HashSet<IPAddress> _addresses = new HashSet<IPAddress>();
        private static long _readAt;
        private static bool _read;

        /// <summary>
        /// <see langword="true"/> if <paramref name="address"/> is a loopback address or one of the addresses of this machine.
        /// </summary>
        public static bool IsLocal(IPAddress address)
        {
            if (address is null)
            {
                return false;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6
                && address.IsIPv4MappedToIPv6)
            {
                address = address.MapToIPv4();
            }

            if (IPAddress.IsLoopback(address))
            {
                return true;
            }

            lock (_lock)
            {
                // read again when the list is older than a second
                if (!_read
                    || Stopwatch.GetTimestamp() - _readAt > Stopwatch.Frequency)
                {
                    Refresh();
                }

                return _addresses.Contains(address);
            }
        }

        private static void Refresh()
        {
            try
            {
                var addresses = new HashSet<IPAddress>();

                foreach (var networkInterface in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (networkInterface.OperationalStatus != OperationalStatus.Up)
                    {
                        continue;
                    }

                    foreach (var unicast in networkInterface.GetIPProperties().UnicastAddresses)
                    {
                        addresses.Add(unicast.Address);
                    }
                }

                _addresses = addresses;
            }
            catch (NetworkInformationException)
            {
                // keep the last list
            }

            _readAt = Stopwatch.GetTimestamp();
            _read = true;
        }
    }
}
