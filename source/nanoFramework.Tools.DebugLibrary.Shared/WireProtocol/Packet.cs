//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Text;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    [Serializable]
    public class Packet
    {
#pragma warning disable S1104 // Fields should not have public accessibility
        // this is required because of the way the de-serialization works in Wire Protocol

        public readonly static string MARKER_DEBUGGER_V1 = "NFDBGV1\0"; // Used to identify the debugger at boot time.
        public readonly static string MARKER_PACKET_V1 = "NFPKTV1\0"; // Used to identify the start of a packet.
        public const int SIZE_OF_MARKER = 8;

        public byte[] Marker = Encoding.UTF8.GetBytes(MARKER_PACKET_V1);
        public uint CrcHeader = 0;
        public uint CrcData = 0;

        public uint Cmd = 0;
        public ushort Seq = 0;
        public ushort SeqReply = 0;
        public uint Flags = 0;
        public uint Size = 0;

#pragma warning restore S1104 // Fields should not have public accessibility

        public Packet()
        {
 
        }
    }
}
