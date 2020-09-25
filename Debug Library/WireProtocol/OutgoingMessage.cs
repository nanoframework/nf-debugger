//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class OutgoingMessage
    {
        internal IController m_parent;

        MessageRaw m_raw;
        MessageBase m_base;

        public OutgoingMessage(IController parent, Converter converter, uint cmd, uint flags, object payload)
        {
            InitializeForSend(parent, converter, cmd, flags, payload);

            UpdateCRC(converter);
        }

        internal OutgoingMessage(IncomingMessage req, Converter converter, uint flags, object payload)
        {
            InitializeForSend(req.Parent, converter, req.Header.m_cmd, flags, payload);

            m_base.m_header.m_seqReply = req.Header.m_seq;
            m_base.m_header.m_flags |= Flags.c_Reply;

            UpdateCRC(converter);
        }

        public Packet Header
        {
            get
            {
                return m_base.m_header;
            }
        }

        public object Payload
        {
            get
            {
                return m_base.m_payload;
            }
        }

        internal void InitializeForSend(IController parent, Converter converter, uint cmd, uint flags, object payload)
        {
            Packet header = parent.NewPacket();

            header.m_cmd = cmd;
            header.m_flags = flags;

            m_parent = parent;

            m_raw = new MessageRaw();
            m_base = new MessageBase();
            m_base.m_header = header;
            m_base.m_payload = payload;

            if (payload != null)
            {
                m_raw.m_payload = converter.Serialize(payload);

                m_base.m_header.m_size = (uint)m_raw.m_payload.Length;
                m_base.m_header.m_crcData = CRC.ComputeCRC(m_raw.m_payload, 0);
            }
        }

        private void UpdateCRC(Converter converter)
        {
            Packet header = m_base.m_header;

            //
            // The CRC for the header is computed setting the CRC field to zero and then running the CRC algorithm.
            //
            header.m_crcHeader = 0;
            header.m_crcHeader = CRC.ComputeCRC(converter.Serialize(header), 0);

            m_raw.m_header = converter.Serialize(header);
        }

        internal async Task<bool> SendAsync()
        {

            DebuggerEventSource.Log.WireProtocolTxHeader(m_base.m_header.m_crcHeader
                                                        , m_base.m_header.m_crcData
                                                        , m_base.m_header.m_cmd
                                                        , m_base.m_header.m_flags
                                                        , m_base.m_header.m_seq
                                                        , m_base.m_header.m_seqReply
                                                        , m_base.m_header.m_size
                                                        );

            return await m_parent.QueueOutputAsync(m_raw).ConfigureAwait(false);
        }
    }
}
