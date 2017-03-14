//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace NanoFramework.Tools.Debugger
{
    public interface IPort
    {
        Task<uint> SendBufferAsync(byte[] buffer, TimeSpan waiTimeout, CancellationToken cancellationToken);

        Task<DataReader> ReadBufferAsync(uint bytesToRead, TimeSpan waiTimeout, CancellationToken cancellationToken);

        Task<bool> ConnectDeviceAsync(NanoDeviceBase device);

        void DisconnectDevice(NanoDeviceBase device);
    }
}
