//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools;
using System;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public interface IControllerHostLocal : IControllerHost
    {
        bool ProcessMessage(IncomingMessage msg, bool fReply);

        Task<uint> SendBufferAsync(byte[] buffer, TimeSpan waiTimeout, CancellationToken cancellationToken);

        Task<DataReader> ReadBufferAsync(uint bytesToRead, TimeSpan waiTimeout, CancellationToken cancellationToken);
    }
}

