//
// Copyright (c) 2017 The nanoFramework project contributors
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
        ushort GetNextSequenceId();

        void StopProcessing();

        void ResumeProcessing();

        uint GetUniqueEndpointId();

        CLRCapabilities Capabilities { get; set; }

        Converter CreateConverter();

        Task<bool> SendAsync(MessageRaw raw, CancellationToken cancellationToken);
    }
}
