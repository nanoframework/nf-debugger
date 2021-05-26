﻿//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public interface IControllerHost
    {
        bool IsConnected { get; }

        bool IsCRC32EnabledForWireProtocol { get; }

        void SpuriousCharacters(byte[] buf, int offset, int count);

        event EventHandler<StringEventArgs> SpuriousCharactersReceived;

        void ProcessExited();
    }
}

