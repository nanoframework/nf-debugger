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
    public class Packet
    {
        public static string MARKER_DEBUGGER_V1 = "MSdbgV1\0"; // Used to identify the debugger at boot time.
        public static string MARKER_PACKET_V1 = "MSpktV1\0"; // Used to identify the start of a packet.
        public const int SIZE_OF_SIGNATURE = 8;

        public byte[] m_signature = new byte[SIZE_OF_SIGNATURE];
        public uint m_crcHeader = 0;
        public uint m_crcData = 0;

        public uint m_cmd = 0;
        public ushort m_seq = 0;
        public ushort m_seqReply = 0;
        public uint m_flags = 0;
        public uint m_size = 0;
    }
}
