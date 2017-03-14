//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using NanoFramework.Tools;
using System.Threading.Tasks;

namespace NanoFramework.Tools.Debugger.WireProtocol
{
    public class IncomingMessage
    {
        IController m_parent;

        MessageRaw m_raw;
        MessageBase m_base;

        public IncomingMessage(IController parent, MessageRaw raw, MessageBase messageBase)
        {
            m_parent = parent;
            m_raw = raw;
            m_base = messageBase;
        }

        public MessageRaw Raw
        {
            get
            {
                return m_raw;
            }
        }

        public MessageBase Base
        {
            get
            {
                return m_base;
            }
        }

        public IController Parent
        {
            get
            {
                return m_parent;
            }
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
            set
            {
                object payload = null;

                if (m_raw.m_payload != null)
                {
                    if (value != null)
                    {
                        new Converter(m_parent.Capabilities).Deserialize(value, m_raw.m_payload);
                        payload = value;
                    }
                    else
                    {
                        payload = m_raw.m_payload.Clone();
                    }
                }

                m_base.m_payload = payload;
            }
        }

        static public bool IsPositiveAcknowledge(IncomingMessage reply)
        {
            return reply != null && ((reply.Header.m_flags & WireProtocol.Flags.c_ACK) != 0);
        }

        static public async Task<bool> ReplyBadPacketAsync(IController ctrl, uint flags)
        {
            //What is this for? Nack + Ping?  What can the nanoCLR possibly do with this information?
            OutgoingMessage msg = new OutgoingMessage(ctrl, new WireProtocol.Converter(), Commands.c_Monitor_Ping, Flags.c_NonCritical | Flags.c_NACK | flags, null);

            return await msg.SendAsync().ConfigureAwait(false);
        }

        public async Task<bool> ReplyAsync(Converter converter, uint flags, object payload)
        {

            OutgoingMessage msgReply = new OutgoingMessage(this, converter, flags, payload);

            return await msgReply.SendAsync().ConfigureAwait(false);
        }
    }
}
