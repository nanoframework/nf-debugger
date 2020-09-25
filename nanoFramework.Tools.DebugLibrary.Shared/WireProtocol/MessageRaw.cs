//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    [Serializable]
    public class MessageRaw
    {
        public byte[] Header;
        public byte[] Payload;
    }
}
