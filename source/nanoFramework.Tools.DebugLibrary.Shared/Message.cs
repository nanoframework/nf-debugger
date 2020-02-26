//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.Debugger
{
    internal class Message
    {
        internal readonly EndPoint m_source;
        internal readonly Commands.Debugging_Messaging_Address m_addr;
        internal readonly byte[] m_payload;

        internal Message(EndPoint source, Commands.Debugging_Messaging_Address addr, byte[] payload)
        {
            m_source = source;
            m_addr = addr;
            m_payload = payload;
        }

        public object Payload
        {
            get
            {
                return m_source._engine.CreateBinaryFormatter().Deserialize(m_payload);
            }
        }

        public void Reply(object data)
        {
            byte[] payload = m_source._engine.CreateBinaryFormatter().Serialize(data);
            m_source.ReplyInner(this, payload);
        }
    }
}
