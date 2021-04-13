﻿//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

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

        bool Send(MessageRaw raw);
    }
}
