//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Threading;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public interface IControllerHostLocal : IControllerHost
    {
        bool ProcessMessage(IncomingMessage msg, bool fReply);

        uint SendBuffer(byte[] buffer, TimeSpan waitTimeout, CancellationToken cancellationToken);

        byte[] ReadBuffer(uint bytesToRead, TimeSpan waitTimeout, CancellationToken cancellationToken);
    }
}

