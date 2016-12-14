//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the Microsoft .NET Micro Framework and is unsupported. 
// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use these files except in compliance with the License.
// You may obtain a copy of the License at:
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing
// permissions and limitations under the License.
// 
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

namespace Microsoft.SPOT.Debugger.WireProtocol
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
