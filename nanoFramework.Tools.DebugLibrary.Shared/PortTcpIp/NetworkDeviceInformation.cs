//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace nanoFramework.Tools.Debugger.PortTcpIp
{
    public class NetworkDeviceInformation
    {
        /// <summary>
        /// Prefix of the id of a network device.
        /// </summary>
        public const string DeviceIdPrefix = "tcpip://";

        /// <summary>
        /// Default TCP port a network device listens on.
        /// </summary>
        public const int DefaultPort = 26000;

        // expected format is "tcpip://{host}:{port}" where host is an IPv4 address or a host name
        private static readonly Regex s_deviceIdRegex = new Regex(
            @"^(?:tcpip://)?(?<host>[A-Za-z0-9](?:[A-Za-z0-9\-\.]*[A-Za-z0-9])?):(?<port>\d{1,5})$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public string Host { get; set; }

        public int Port { get; set; }

        /// <summary>
        /// Name of the target, as announced by the device. Empty if the announcement has none.
        /// </summary>
        public string TargetName { get; set; } = string.Empty;

        /// <summary>
        /// Id of the device, in the form <c>tcpip://host:port</c>.
        /// </summary>
        public string DeviceId => $"{DeviceIdPrefix}{Host}:{Port}";

        /// <summary>
        /// The key for the exclusive access to this device. It's the same everywhere a lock on the device is taken.
        /// </summary>
        public string ExclusiveAccessKey => DeviceId;

        public NetworkDeviceInformation(string host, int port)
        {
            // host names are case insensitive: normalize so the same device gets the same id
            Host = host?.ToLowerInvariant();
            Port = port;
        }

        /// <summary>
        /// Parses a device id, <c>tcpip://host:port</c> (the <c>tcpip://</c> prefix is optional).
        /// </summary>
        /// <param name="deviceId">The device id.</param>
        /// <param name="info">The device information, if the id is valid.</param>
        /// <returns><see langword="true"/> if <paramref name="deviceId"/> is valid.</returns>
        public static bool TryParse(string deviceId, out NetworkDeviceInformation info)
        {
            info = null;

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return false;
            }

            var match = s_deviceIdRegex.Match(deviceId.Trim());
            if (!match.Success
                || !int.TryParse(match.Groups["port"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                || port < 1
                || port > 65535)
            {
                return false;
            }

            info = new NetworkDeviceInformation(match.Groups["host"].Value, port);

            return true;
        }

        /// <summary>
        /// Checks if a device id is a network device id, that is, if <see cref="TryParse"/> accepts it
        /// (the <c>tcpip://</c> prefix is optional).
        /// </summary>
        public static bool IsNetworkDeviceId(string deviceId) =>
            TryParse(deviceId, out _);
    }
}
