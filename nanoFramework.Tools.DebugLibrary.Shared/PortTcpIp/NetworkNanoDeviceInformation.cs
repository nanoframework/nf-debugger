//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger.PortTcpIp
{
    public class NetworkNanoDeviceInformation
    {
        public string Host { get; set; }
        public int Port { get; set; }
        
        public string DeviceId => $"tcpip://{Host}:{Port}";

        public NetworkNanoDeviceInformation(string host, int port)
        {
            Host = host;
            Port = port;
        }
    }
}