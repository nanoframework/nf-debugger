//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger.PortTcpIp
{
    public class NetworkDeviceInformation
    {
        public string Host { get; set; }
        public int Port { get; set; }

        public string DeviceId => $"tcpip://{Host}:{Port}";

        public NetworkDeviceInformation(string host, int port)
        {
            Host = host;
            Port = port;
        }
    }
}