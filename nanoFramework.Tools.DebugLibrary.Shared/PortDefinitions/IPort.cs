//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger
{
    public interface IPort
    {
        uint SendBuffer(byte[] buffer, TimeSpan waiTimeout, CancellationToken cancellationToken);

        byte[] ReadBuffer(uint bytesToRead, TimeSpan waiTimeout, CancellationToken cancellationToken);

        Task<bool> ConnectDevice();

        void DisconnectDevice();
    }
}
