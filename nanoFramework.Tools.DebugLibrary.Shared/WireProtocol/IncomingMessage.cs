//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class IncomingMessage
    {
        readonly IController _parent;

        readonly MessageRaw _raw;
        readonly MessageBase _base;

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
    }
}
