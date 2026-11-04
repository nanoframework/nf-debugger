//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Globalization;
using System.Net;

namespace nanoFramework.Tools.Debugger.PortTcpIp
{
    /// <summary>
    /// A device announcement received on the discovery port.
    /// </summary>
    /// <remarks>
    /// The messages are ASCII text:
    /// <list type="bullet">
    /// <item><description><c>+:&lt;port&gt;:&lt;targetName&gt;</c> the device is available.</description></item>
    /// <item><description><c>-:&lt;port&gt;</c> the device is gone.</description></item>
    /// </list>
    /// The address of the device is the one of the sender of the datagram.
    /// </remarks>
    public sealed class DiscoveryMessage
    {
        private const char Separator = ':';

        /// <summary>
        /// <see langword="true"/> for an announcement (<c>+</c>), <see langword="false"/> for a departure (<c>-</c>).
        /// </summary>
        public bool IsAnnouncement { get; }

        public int Port { get; }

        public string TargetName { get; }

        private DiscoveryMessage(bool isAnnouncement, int port, string targetName)
        {
            IsAnnouncement = isAnnouncement;
            Port = port;
            TargetName = targetName;
        }

        /// <summary>
        /// Parses a discovery message.
        /// </summary>
        /// <returns><see langword="false"/> if the message isn't a valid discovery message.</returns>
        public static bool TryParse(string message, out DiscoveryMessage result)
        {
            result = null;

            if (string.IsNullOrEmpty(message))
            {
                return false;
            }

            var tokens = message.Trim('\0', '\r', '\n', ' ').Split(Separator);

            // "-:<port>"
            if (tokens.Length == 2 && tokens[0] == "-")
            {
                if (!TryParsePort(tokens[1], out var stopPort))
                {
                    return false;
                }

                result = new DiscoveryMessage(false, stopPort, string.Empty);

                return true;
            }

            // "+:<port>:<targetName>": anything after the port is the target name
            if (tokens.Length >= 3 && tokens[0] == "+")
            {
                if (!TryParsePort(tokens[1], out var port))
                {
                    return false;
                }

                var targetName = string.Join(
                    Separator.ToString(),
                    tokens,
                    2,
                    tokens.Length - 2);

                result = new DiscoveryMessage(true, port, targetName);

                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the information of the device that sent this message.
        /// </summary>
        /// <param name="sender">Address of the sender of the datagram.</param>
        public NetworkDeviceInformation ToDeviceInformation(IPAddress sender)
        {
            // an IPv4 address that arrives on a dual mode socket is mapped to IPv6
            if (sender.IsIPv4MappedToIPv6)
            {
                sender = sender.MapToIPv4();
            }

            return new NetworkDeviceInformation(sender.ToString(), Port)
            {
                TargetName = TargetName
            };
        }

        private static bool TryParsePort(string token, out int port) =>
            int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out port)
            && port >= 1
            && port <= 65535;
    }
}
