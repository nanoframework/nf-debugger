//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger
{
    [Flags]
    public enum EraseOptions
    {
        Deployment = 0x01,
        UserStorage = 0x02,
        FileSystem = 0x04,
        Firmware = 0x08,
        UpdateStorage = 0x10,
        SimpleStorage = 0x20,
        Configuration = 0x40,

        All = SimpleStorage | UpdateStorage | Firmware | FileSystem | UserStorage | Deployment | Configuration
    }
}
