//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System.Collections.Generic;

namespace nanoFramework.Tools.Debugger
{
    internal class SrecParseResult
    {
        public uint EntryPoint { get; internal set; }
        public uint ImageSize { get; internal set; }
        public Dictionary<uint, string> Records { get; set; } = new Dictionary<uint, string>();
    }
}
