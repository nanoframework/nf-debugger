//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public interface IControllerHostLocal : IControllerHost
    {
        bool ProcessMessage(IncomingMessage msg, bool fReply);

        int SendBuffer(byte[] buffer);

        byte[] ReadBuffer(int bytesToRead);

        int AvailableBytes { get; }

        void ReplyBadPacket(uint flags);
    }
}

