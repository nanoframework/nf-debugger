//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Net.Sockets;

namespace nanoFramework.Tools.Debugger.PortTcpIp
{
    public class NanoNetworkDevice: IDisposable
    {
        private NetworkStream _networkStream;
        
        private TcpClient _networkClient;
        
        public const int SafeDefaultTimeout = 1000;        

        public bool Connected => _networkClient?.Connected == true;
        
        public int AvailableBytes => _networkClient?.Connected == true ? _networkClient.Available : -1; 
        
        public NetworkDeviceInformation NetworkDeviceInformation { get; set; }

        public NetworkStream Connect()
        {
            if(_networkClient?.Connected == true)
                return _networkStream;

            _networkClient = new TcpClient(NetworkDeviceInformation.Host, NetworkDeviceInformation.Port);
            _networkStream = _networkClient.GetStream();
            return _networkStream;
        }

        public void Close()
        {
            _networkStream?.Close();
            _networkClient?.Close();
        }

        public void Dispose()
        {
            _networkStream?.Dispose();
            _networkClient?.Dispose();
        }
    }
}