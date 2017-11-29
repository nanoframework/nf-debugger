//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class OutgoingMessage
    {
        ushort _sequenceId;

        public MessageRaw Raw { get; private set; }
        public MessageBase Base { get; private set; }

        public Packet Header => Base.Header;

        public object Payload => Base.Payload;

        public OutgoingMessage(ushort sequenceId, Converter converter, uint command, uint flags, object payload)
        {
            _sequenceId = sequenceId;

            InitializeForSend(converter, command, flags, payload);

            UpdateCRC(converter);
        }

        internal OutgoingMessage(IncomingMessage request, Converter converter, uint flags, object payload)
        {
            InitializeForSend(converter, request.Header.Cmd, flags, payload);

            Base.Header.SeqReply = request.Header.Seq;
            Base.Header.Flags |= Flags.c_Reply;

            UpdateCRC(converter);
        }


        internal void InitializeForSend(Converter converter, uint cmd, uint flags, object payload)
        {
            Packet header = new Packet
            {
                Seq = _sequenceId,
                Cmd = cmd,
                Flags = flags
            };

            Raw = new MessageRaw();

            Base = new MessageBase
            {
                Header = header,
                Payload = payload
            };

            if (payload != null)
            {
                Raw.Payload = converter.Serialize(payload);

                Base.Header.Size = (uint)Raw.Payload.Length;
                Base.Header.CrcData = CRC.ComputeCRC(Raw.Payload, 0);
            }
        }

        private void UpdateCRC(Converter converter)
        {
            Packet header = Base.Header;

            //
            // The CRC for the header is computed setting the CRC field to zero and then running the CRC algorithm.
            //
            header.CrcHeader = 0;
            header.CrcHeader = CRC.ComputeCRC(converter.Serialize(header), 0);

            Raw.Header = converter.Serialize(header);
        }
    }
}
