//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class Packet
    {
        public static string MARKER_DEBUGGER_V1 = "NFDBGV1\0"; // Used to identify the debugger at boot time.
        public static string MARKER_PACKET_V1 = "NFPKTV1\0"; // Used to identify the start of a packet.
        public const int SIZE_OF_SIGNATURE = 8;

        public byte[] Signature = new byte[SIZE_OF_SIGNATURE];
        public uint CrcHeader = 0;
        public uint CrcData = 0;

        public uint Cmd = 0;
        public ushort Seq = 0;
        public ushort SeqReply = 0;
        public uint Flags = 0;
        public uint Size = 0;
    }
}
