//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;

namespace NanoFramework.ANT.Models
{
    public enum ConnectionState
    {
        None = 0,
        Connected,
        Connecting,
        Disconnected,
        Disconnecting
    }
}
