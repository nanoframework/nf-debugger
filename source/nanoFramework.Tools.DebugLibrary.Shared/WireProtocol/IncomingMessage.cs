//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class IncomingMessage
    {
        IController _parent;

        MessageRaw _raw;
        MessageBase _base;

        public IncomingMessage(IController parent, MessageRaw raw, MessageBase messageBase)
        {
            _parent = parent;
            _raw = raw;
            _base = messageBase;
        }

        public MessageRaw Raw => _raw;

        public MessageBase Base => _base;

        public IController Parent => _parent;

        public Packet Header => _base.Header;

        public object Payload
        {
            get
            {
                return _base.Payload;
            }
            set
            {
                object payload = null;

                if (_raw.Payload != null)
                {
                    if (value != null)
                    {
                        new Converter(_parent.Capabilities).Deserialize(value, _raw.Payload);
                        payload = value;
                    }
                    else
                    {
                        payload = _raw.Payload.Clone();
                    }
                }

                _base.Payload = payload;
            }
        }

        static public bool IsPositiveAcknowledge(IncomingMessage reply)
        {
            return reply != null && ((reply.Header.Flags & WireProtocol.Flags.c_ACK) != 0);
        }

        public async Task<bool> ReplyAsync(Converter converter, uint flags, object payload, CancellationToken cancellationToken)
        {
            // FIXME

            //OutgoingMessage msgReply = new OutgoingMessage(this, converter, flags, payload);

            //return await msgReply.SendAsync(cancellationToken);
            return false;
        }
    }
}
