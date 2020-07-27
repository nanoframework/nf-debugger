//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public interface IControllerHostLocal : IControllerHost
    {
        bool ProcessMessage(IncomingMessage msg, bool fReply);

        Task<uint> SendBufferAsync(byte[] buffer, TimeSpan waitTimeout, CancellationToken cancellationToken);

        Task<byte[]> ReadBufferAsync(uint bytesToRead, TimeSpan waitTimeout, CancellationToken cancellationToken);
    }
}

