//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class Flags
    {
        public const ushort c_NonCritical = 0x0001; // This doesn't need an acknowledge.
        public const ushort c_Reply = 0x0002; // This is the result of a command.
        public const ushort c_BadHeader = 0x0004;
        public const ushort c_BadPayload = 0x0008;
        public const ushort c_Spare0010 = 0x0010;
        public const ushort c_Spare0020 = 0x0020;
        public const ushort c_Spare0040 = 0x0040;
        public const ushort c_Spare0080 = 0x0080;
        public const ushort c_Spare0100 = 0x0100;
        public const ushort c_Spare0200 = 0x0200;
        public const ushort c_Spare0400 = 0x0400;
        public const ushort c_Spare0800 = 0x0800;
        public const ushort c_Spare1000 = 0x1000;
        public const ushort c_NoCaching = 0x2000;
        public const ushort c_NACK = 0x4000;
        public const ushort c_ACK = 0x8000;
    }
}
