//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger.WireProtocol
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
