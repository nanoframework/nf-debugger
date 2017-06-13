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
    public class OutgoingMessage
    {
        internal IController _parent;

        MessageRaw _raw;
        MessageBase _base;

        public OutgoingMessage(IController parent, Converter converter, uint cmd, uint flags, object payload)
        {
            InitializeForSend(parent, converter, cmd, flags, payload);

            UpdateCRC(converter);
        }

        internal OutgoingMessage(IncomingMessage req, Converter converter, uint flags, object payload)
        {
            InitializeForSend(req.Parent, converter, req.Header.Cmd, flags, payload);

            _base.Header.SeqReply = req.Header.Seq;
            _base.Header.Flags |= Flags.c_Reply;

            UpdateCRC(converter);
        }

        public Packet Header
        {
            get
            {
                return _base.Header;
            }
        }

        public object Payload
        {
            get
            {
                return _base.Payload;
            }
        }

        internal void InitializeForSend(IController parent, Converter converter, uint cmd, uint flags, object payload)
        {
            Packet header = parent.NewPacket();

            header.Cmd = cmd;
            header.Flags = flags;

            _parent = parent;

            _raw = new MessageRaw();
            _base = new MessageBase();
            _base.Header = header;
            _base.Payload = payload;

            if (payload != null)
            {
                _raw.Payload = converter.Serialize(payload);

                _base.Header.Size = (uint)_raw.Payload.Length;
                _base.Header.CrcData = CRC.ComputeCRC(_raw.Payload, 0);
            }
        }

        private void UpdateCRC(Converter converter)
        {
            Packet header = _base.Header;

            //
            // The CRC for the header is computed setting the CRC field to zero and then running the CRC algorithm.
            //
            header.CrcHeader = 0;
            header.CrcHeader = CRC.ComputeCRC(converter.Serialize(header), 0);

            _raw.Header = converter.Serialize(header);
        }

        internal async Task<bool> SendAsync(CancellationToken cancellationToken)
        {

            DebuggerEventSource.Log.WireProtocolTxHeader(_base.Header.CrcHeader
                                                        , _base.Header.CrcData
                                                        , _base.Header.Cmd
                                                        , _base.Header.Flags
                                                        , _base.Header.Seq
                                                        , _base.Header.SeqReply
                                                        , _base.Header.Size
                                                        );

            return await _parent.QueueOutputAsync(_raw, cancellationToken);
        }
    }
}
