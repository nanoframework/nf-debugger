//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public interface IController
    {
        Packet NewPacket();

        void StopProcessing();

        void ResumeProcessing();

        uint GetUniqueEndpointId();

        CLRCapabilities Capabilities { get; set; }

        Converter CreateConverter();

        Task<bool> QueueOutputAsync(MessageRaw raw);

        Task<uint> SendRawBufferAsync(byte[] buffer, TimeSpan waiTimeout, CancellationToken cancellationToken);
    }
}
