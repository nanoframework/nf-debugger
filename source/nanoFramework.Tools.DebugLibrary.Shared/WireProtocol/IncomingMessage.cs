//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools;
using System.Threading.Tasks;
using System.Threading;

namespace nanoFramework.Tools.Debugger.WireProtocol
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
                return m_base.Header;
            }
        }

        public object Payload
        {
            get
            {
                return m_base.Payload;
            }
            set
            {
                object payload = null;

                if (m_raw.Payload != null)
                {
                    if (value != null)
                    {
                        new Converter(m_parent.Capabilities).Deserialize(value, m_raw.Payload);
                        payload = value;
                    }
                    else
                    {
                        payload = m_raw.Payload.Clone();
                    }
                }

                m_base.Payload = payload;
            }
        }

        static public bool IsPositiveAcknowledge(IncomingMessage reply)
        {
            return reply != null && ((reply.Header.Flags & WireProtocol.Flags.c_ACK) != 0);
        }

        static public async Task<bool> ReplyBadPacketAsync(IController ctrl, uint flags, CancellationToken cancellationToken)
        {
            //What is this for? Nack + Ping?  What can the nanoCLR possibly do with this information?
            OutgoingMessage msg = new OutgoingMessage(ctrl, new WireProtocol.Converter(), Commands.c_Monitor_Ping, Flags.c_NonCritical | Flags.c_NACK | flags, null);

            return await msg.SendAsync(cancellationToken);
        }

        public async Task<bool> ReplyAsync(Converter converter, uint flags, object payload, CancellationToken cancellationToken)
        {

            OutgoingMessage msgReply = new OutgoingMessage(this, converter, flags, payload);

            return await msgReply.SendAsync(cancellationToken);
        }
    }
}
