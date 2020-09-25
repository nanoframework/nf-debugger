//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.Extensions;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    internal class MessageReassembler
    {
        internal byte[] marker_Debugger = Encoding.UTF8.GetBytes(Packet.MARKER_DEBUGGER_V1);
        internal byte[] marker_Packet = Encoding.UTF8.GetBytes(Packet.MARKER_PACKET_V1);

        internal enum ReceiveState
        {
            Idle = 0,
            Initialize = 1,
            WaitingForHeader = 2,
            ReadingHeader = 3,
            CompleteHeader = 4,
            ReadingPayload = 5,
            CompletePayload = 6,
        }

        Controller m_parent;
        ReceiveState m_state;

        MessageRaw m_raw;
        int m_rawPos;
        MessageBase m_base;
        private Request request;

        internal MessageReassembler(Controller parent)
        {
            m_parent = parent;
            m_state = ReceiveState.Initialize;
        }

        internal MessageReassembler(Controller parent, Request request)
        {
            this.request = request;
            m_parent = parent;
            m_state = ReceiveState.Initialize;
        }

        internal IncomingMessage GetCompleteMessage()
        {
            return new IncomingMessage(m_parent, m_raw, m_base);
        }

        /// <summary>
        /// Essential Rx method. Drives state machine by reading data and processing it. This works in
        /// conjunction with NotificationThreadWorker [Tx].
        /// </summary>
        internal async Task<IncomingMessage> ProcessAsync(CancellationToken cancellationToken)
        {
            int count;
            int bytesRead;

            try
            {

                switch (m_state)
                {
                    case ReceiveState.Initialize:

                        if (cancellationToken.IsCancellationRequested)
                        {
                            // cancellation requested

                            Debug.WriteLine("cancel token");

                            return null;
                        }

                        m_rawPos = 0;

                        m_base = new MessageBase();
                        m_base.m_header = new Packet();

                        m_raw = new MessageRaw();
                        m_raw.m_header = m_parent.CreateConverter().Serialize(m_base.m_header);

                        m_state = ReceiveState.WaitingForHeader;
                        DebuggerEventSource.Log.WireProtocolReceiveState(m_state);
                        goto case ReceiveState.WaitingForHeader;

                    case ReceiveState.WaitingForHeader:
                        count = m_raw.m_header.Length - m_rawPos;

                        Debug.WriteLine("WaitingForHeader");

                        // need to have a timeout to cancel the read task otherwise it may end up waiting forever for this to return
                        // because we have an external cancellation token and the above timeout cancellation token, need to combine both

                        bytesRead = await m_parent.ReadBufferAsync(m_raw.m_header, m_rawPos, count, request.waitRetryTimeout, cancellationToken.AddTimeout(request.waitRetryTimeout)).ConfigureAwait(false);

                        m_rawPos += bytesRead;

                        // sanity check
                        if (bytesRead != 32)
                        {
                            // doesn't look like a header, better restart
                            m_state = ReceiveState.Initialize;
                            goto case ReceiveState.Initialize;
                        }

                        while (m_rawPos > 0)
                        {
                            int flag_Debugger = ValidSignature(marker_Debugger);
                            int flag_Packet = ValidSignature(marker_Packet);

                            if (flag_Debugger == 1 || flag_Packet == 1)
                            {
                                m_state = ReceiveState.ReadingHeader;
                                DebuggerEventSource.Log.WireProtocolReceiveState(m_state);
                                goto case ReceiveState.ReadingHeader;
                            }

                            if (flag_Debugger == 0 || flag_Packet == 0)
                            {
                                break; // Partial match.
                            }

                            m_parent.App.SpuriousCharacters(m_raw.m_header, 0, 1);

                            Array.Copy(m_raw.m_header, 1, m_raw.m_header, 0, --m_rawPos);
                        }
                        break;

                    case ReceiveState.ReadingHeader:
                        count = m_raw.m_header.Length - m_rawPos;

                        Debug.WriteLine("ReadingHeader");

                        // need to have a timeout to cancel the read task otherwise it may end up waiting forever for this to return
                        // because we have an external cancellation token and the above timeout cancellation token, need to combine both

                        bytesRead = await m_parent.ReadBufferAsync(m_raw.m_header, m_rawPos, count, request.waitRetryTimeout, cancellationToken.AddTimeout(request.waitRetryTimeout)).ConfigureAwait(false);

                        m_rawPos += bytesRead;

                        if (bytesRead != count) break;

                        m_state = ReceiveState.CompleteHeader;
                        DebuggerEventSource.Log.WireProtocolReceiveState(m_state);
                        goto case ReceiveState.CompleteHeader;
                    //break;

                    case ReceiveState.CompleteHeader:
                        try
                        {
                            Debug.WriteLine("CompleteHeader");

                            m_parent.CreateConverter().Deserialize(m_base.m_header, m_raw.m_header);

                            if (VerifyHeader() == true)
                            {
                                Debug.WriteLine("CompleteHeader, header OK");

                                bool fReply = (m_base.m_header.m_flags & Flags.c_Reply) != 0;

                                DebuggerEventSource.Log.WireProtocolRxHeader(m_base.m_header.m_crcHeader, m_base.m_header.m_crcData, m_base.m_header.m_cmd, m_base.m_header.m_flags, m_base.m_header.m_seq, m_base.m_header.m_seqReply, m_base.m_header.m_size);

                                if (m_base.m_header.m_size != 0)
                                {
                                    m_raw.m_payload = new byte[m_base.m_header.m_size];
                                    //reuse m_rawPos for position in header to read.
                                    m_rawPos = 0;

                                    m_state = ReceiveState.ReadingPayload;
                                    DebuggerEventSource.Log.WireProtocolReceiveState(m_state);
                                    goto case ReceiveState.ReadingPayload;
                                }
                                else
                                {
                                    m_state = ReceiveState.CompletePayload;
                                    DebuggerEventSource.Log.WireProtocolReceiveState(m_state);
                                    goto case ReceiveState.CompletePayload;
                                }
                            }

                            Debug.WriteLine("CompleteHeader, header not valid");
                        }
                        //catch (ThreadAbortException)
                        //{
                        //    throw;
                        //}
                        catch (Exception e)
                        {
                            Debug.WriteLine("Fault at payload deserialization:\n\n{0}", e.ToString());
                        }

                        m_state = ReceiveState.Initialize;
                        DebuggerEventSource.Log.WireProtocolReceiveState(m_state);

                        if ((m_base.m_header.m_flags & Flags.c_NonCritical) == 0)
                        {
                            // FIXME 
                            // evaluate the purpose of this reply back to the NanoFramework device, the nanoCLR doesn't seem to have to handle this. In the end it looks like this does have any real purpose and will only be wasting CPU.
                            //await IncomingMessage.ReplyBadPacketAsync(m_parent, Flags.c_BadHeader).ConfigureAwait(false);
                            return null;
                        }

                        break;

                    case ReceiveState.ReadingPayload:
                        count = m_raw.m_payload.Length - m_rawPos;

                        Debug.WriteLine("ReadingPayload");

                        // need to have a timeout to cancel the read task otherwise it may end up waiting forever for this to return
                        // because we have an external cancellation token and the above timeout cancellation token, need to combine both

                        bytesRead = await m_parent.ReadBufferAsync(m_raw.m_payload, m_rawPos, count, request.waitRetryTimeout, cancellationToken.AddTimeout(request.waitRetryTimeout)).ConfigureAwait(false);

                        m_rawPos += bytesRead;

                        if (bytesRead != count) break;

                        m_state = ReceiveState.CompletePayload;
                        DebuggerEventSource.Log.WireProtocolReceiveState(m_state);
                        goto case ReceiveState.CompletePayload;

                    case ReceiveState.CompletePayload:
                        Debug.WriteLine("CompletePayload");

                        if (VerifyPayload() == true)
                        {
                            Debug.WriteLine("CompletePayload payload OK");

                            try
                            {
                                bool fReply = (m_base.m_header.m_flags & Flags.c_Reply) != 0;

                                if ((m_base.m_header.m_flags & Flags.c_NACK) != 0)
                                {
                                    m_raw.m_payload = null;
                                }

                                if (await ProcessMessage(GetCompleteMessage(), fReply).ConfigureAwait(false))
                                {
                                    DebuggerEventSource.Log.WireProtocolReceiveState(m_state);

                                    //Debug.WriteLine("*** leaving reassembler");

                                    return GetCompleteMessage();
                                }
                                else
                                {
                                    // this is not the message we were waiting 
                                    // FIXME
                                }
                                //m_parent.App.ProcessMessage(this.GetCompleteMessage(), fReply);

                                //m_state = ReceiveState.Initialize;
                                //return;
                            }
                            //catch (ThreadAbortException)
                            //{
                            //    throw;
                            //}
                            catch (Exception e)
                            {
                                Debug.WriteLine("Fault at payload deserialization:\n\n{0}", e.ToString());
                            }
                        }
                        else
                        {
                            Debug.WriteLine("CompletePayload payload not valid");
                        }

                        m_state = ReceiveState.Initialize;
                        DebuggerEventSource.Log.WireProtocolReceiveState(m_state);

                        if ((m_base.m_header.m_flags & Flags.c_NonCritical) == 0)
                        {
                            // FIXME 
                            // evaluate the purpose of this reply back to the NanoFramework device, the nanoCLR doesn't seem to have to handle this. In the end it looks like this does have any real purpose and will only be wasting CPU.
                            await IncomingMessage.ReplyBadPacketAsync(m_parent, Flags.c_BadPayload).ConfigureAwait(false);
                            return null;
                        }

                        break;
                }          
            }
            catch
            {
                m_state = ReceiveState.Initialize;
                DebuggerEventSource.Log.WireProtocolReceiveState(m_state);
                Debug.WriteLine("*** EXCEPTION ***");
                throw;
            }

            Debug.WriteLine("??????? leaving reassembler");
            return null;
        }

        private int ValidSignature(byte[] sig)
        {
            System.Diagnostics.Debug.Assert(sig != null && sig.Length == Packet.SIZE_OF_SIGNATURE);
            int markerSize = Packet.SIZE_OF_SIGNATURE;
            int iMax = System.Math.Min(m_rawPos, markerSize);

            for (int i = 0; i < iMax; i++)
            {
                if (m_raw.m_header[i] != sig[i]) return -1;
            }

            if (m_rawPos < markerSize) return 0;

            return 1;
        }

        private bool VerifyHeader()
        {
            uint crc = m_base.m_header.m_crcHeader;
            bool fRes;

            m_base.m_header.m_crcHeader = 0;

            fRes = CRC.ComputeCRC(m_parent.CreateConverter().Serialize(m_base.m_header), 0) == crc;

            m_base.m_header.m_crcHeader = crc;

            return fRes;
        }

        private bool VerifyPayload()
        {
            if (m_raw.m_payload == null)
            {
                return (m_base.m_header.m_size == 0);
            }
            else
            {
                if (m_base.m_header.m_size != m_raw.m_payload.Length) return false;

                return CRC.ComputeCRC(m_raw.m_payload, 0) == m_base.m_header.m_crcData;
            }
        }

        public async Task<bool> ProcessMessage(IncomingMessage msg, bool fReply)
        {
            msg.Payload = Commands.ResolveCommandToPayload(msg.Header.m_cmd, fReply, m_parent.Capabilities);

            if (fReply == true)
            {
                Request reply = null;

                if (request.MatchesReply(msg))
                {
                    reply = request;

                    // FIXME: check if this return can happen here without the QueueNotify call bellow
                    return true;
                }
                else
                if (reply != null)
                {
                    // FIXME
                    reply.Signal(msg);
                    return true;
                }
            }
            else
            {
                Packet bp = msg.Header;

                switch (bp.m_cmd)
                {
                    case Commands.c_Monitor_Ping:
                        {
                            Commands.Monitor_Ping.Reply cmdReply = new Commands.Monitor_Ping.Reply();

                            cmdReply.m_source = Commands.Monitor_Ping.c_Ping_Source_Host;
                            
                            // FIXME
                            //cmdReply.m_dbg_flags = (m_stopDebuggerOnConnect ? Commands.Monitor_Ping.c_Ping_DbgFlag_Stop : 0);

                            await msg.ReplyAsync(m_parent.CreateConverter(), Flags.c_NonCritical, cmdReply).ConfigureAwait(false);

                            //m_evtPing.Set();

                            return true;
                        }

                    case Commands.c_Monitor_Message:
                        {
                            Commands.Monitor_Message payload = msg.Payload as Commands.Monitor_Message;

                            Debug.Assert(payload != null);

                            if (payload != null)
                            {
                                // FIXME
                                //QueueNotify(m_eventMessage, msg, payload.ToString());
                            }

                            return true;
                        }

                    case Commands.c_Debugging_Messaging_Query:
                    case Commands.c_Debugging_Messaging_Reply:
                    case Commands.c_Debugging_Messaging_Send:
                        {
                            Debug.Assert(msg.Payload != null);

                            if (msg.Payload != null)
                            {
                                // FIXME
                                //QueueRpc(msg);
                            }

                            return true;
                        }
                }
            }

            // FIXME
            //if (m_eventCommand != null)
            //{
            //    QueueNotify(m_eventCommand, msg, fReply);
            //    return true;
            //}

            return false;
        }

    }
}
