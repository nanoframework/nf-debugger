//
// Copyright (c) 2017 The nanoFramework project contributors
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
        internal byte[] markerDebugger = Encoding.UTF8.GetBytes(Packet.MARKER_DEBUGGER_V1);
        internal byte[] markerPacket = Encoding.UTF8.GetBytes(Packet.MARKER_PACKET_V1);

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

        Controller _parent;
        ReceiveState _state;

        MessageRaw _messageRaw;
        int _rawPos;
        MessageBase _messageBase;
        private Request request;

        internal MessageReassembler(Controller parent)
        {
            _parent = parent;
            _state = ReceiveState.Initialize;
        }

        internal MessageReassembler(Controller parent, Request request)
        {
            this.request = request;
            _parent = parent;
            _state = ReceiveState.Initialize;
        }

        internal IncomingMessage GetCompleteMessage()
        {
            return new IncomingMessage(_parent, _messageRaw, _messageBase);
        }

        /// <summary>
        /// Essential Rx method. Drives state machine by reading data and processing it. This works in
        /// conjunction with NotificationThreadWorker [Tx].
        /// </summary>
        internal async Task<IncomingMessage> ProcessAsync(CancellationToken cancellationToken)
        {
            int count;
            int bytesRead;

            // compute operation timeout here to ease reuse on calls bellow
            TimeSpan operationTimeout = request != null ? request.waitRetryTimeout : new TimeSpan(500);

            try
            {
                while (true)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        // cancellation requested
                        return GetCompleteMessage();
                    }

                    switch (_state)
                    {
                        case ReceiveState.Initialize:
                            _rawPos = 0;

                            _messageBase = new MessageBase();
                            _messageBase.Header = new Packet();

                            _messageRaw = new MessageRaw();
                            _messageRaw.Header = _parent.CreateConverter().Serialize(_messageBase.Header);

                            _state = ReceiveState.WaitingForHeader;
                            DebuggerEventSource.Log.WireProtocolReceiveState(_state);
                            break;

                        case ReceiveState.WaitingForHeader:
                            count = _messageRaw.Header.Length - _rawPos;

                            // need to have a timeout to cancel the read task otherwise it may end up waiting forever for this to return
                            // because we have an external cancellation token and the above timeout cancellation token, need to combine both
                            bytesRead = await _parent.ReadBufferAsync(_messageRaw.Header, _rawPos, count, operationTimeout, cancellationToken.AddTimeout(operationTimeout));

                            _rawPos += bytesRead;

                            // sanity check
                            if (bytesRead != 32)
                            {
                                // doesn't look like a header, better restart
                                _state = ReceiveState.Initialize;
                                break;
                            }

                            while (_rawPos > 0)
                            {
                                int flag_Debugger = ValidMarker(markerDebugger);
                                int flag_Packet = ValidMarker(markerPacket);

                                if (flag_Debugger == 1 || flag_Packet == 1)
                                {
                                    _state = ReceiveState.ReadingHeader;
                                    DebuggerEventSource.Log.WireProtocolReceiveState(_state);
                                    break;
                                }

                                if (flag_Debugger == 0 || flag_Packet == 0)
                                {
                                    break; // Partial match.
                                }

                                _parent.App.SpuriousCharacters(_messageRaw.Header, 0, 1);

                                Array.Copy(_messageRaw.Header, 1, _messageRaw.Header, 0, --_rawPos);
                            }
                            break;

                        case ReceiveState.ReadingHeader:
                            count = _messageRaw.Header.Length - _rawPos;

                            // need to have a timeout to cancel the read task otherwise it may end up waiting forever for this to return
                            // because we have an external cancellation token and the above timeout cancellation token, need to combine both
                            bytesRead = await _parent.ReadBufferAsync(_messageRaw.Header, _rawPos, count, operationTimeout, cancellationToken.AddTimeout(operationTimeout));

                            _rawPos += bytesRead;

                            if (bytesRead != count) break;

                            _state = ReceiveState.CompleteHeader;
                            DebuggerEventSource.Log.WireProtocolReceiveState(_state);
                            break;

                        case ReceiveState.CompleteHeader:
                            try
                            {
                                _parent.CreateConverter().Deserialize(_messageBase.Header, _messageRaw.Header);

                                if (VerifyHeader() == true)
                                {
                                    bool fReply = (_messageBase.Header.Flags & Flags.c_Reply) != 0;

                                    DebuggerEventSource.Log.WireProtocolRxHeader(_messageBase.Header.CrcHeader, _messageBase.Header.CrcData, _messageBase.Header.Cmd, _messageBase.Header.Flags, _messageBase.Header.Seq, _messageBase.Header.SeqReply, _messageBase.Header.Size);

                                    if (_messageBase.Header.Size != 0)
                                    {
                                        _messageRaw.Payload = new byte[_messageBase.Header.Size];
                                        //reuse m_rawPos for position in header to read.
                                        _rawPos = 0;

                                        _state = ReceiveState.ReadingPayload;
                                        DebuggerEventSource.Log.WireProtocolReceiveState(_state);
                                        break;
                                    }
                                    else
                                    {
                                        _state = ReceiveState.CompletePayload;
                                        DebuggerEventSource.Log.WireProtocolReceiveState(_state);
                                        break;
                                    }
                                }
                            }
                            catch (Exception e)
                            {
                                Debug.WriteLine("Fault at payload deserialization:\n\n{0}", e.ToString());
                            }

                            _state = ReceiveState.Initialize;
                            DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                            if ((_messageBase.Header.Flags & Flags.c_NonCritical) == 0)
                            {
                                // FIXME 
                                // evaluate the purpose of this reply back to the nanoFramework device, the nanoCLR doesn't seem to have to handle this. In the end it looks like this does have any real purpose and will only be wasting CPU.
                                await IncomingMessage.ReplyBadPacketAsync(_parent, Flags.c_BadHeader, cancellationToken);
                                return GetCompleteMessage();
                            }

                            break;

                        case ReceiveState.ReadingPayload:
                            count = _messageRaw.Payload.Length - _rawPos;

                            // need to have a timeout to cancel the read task otherwise it may end up waiting forever for this to return
                            // because we have an external cancellation token and the above timeout cancellation token, need to combine both
                            bytesRead = await _parent.ReadBufferAsync(_messageRaw.Payload, _rawPos, count, operationTimeout, cancellationToken.AddTimeout(operationTimeout));

                            _rawPos += bytesRead;

                            if (bytesRead != count) break;

                            _state = ReceiveState.CompletePayload;
                            DebuggerEventSource.Log.WireProtocolReceiveState(_state);
                            break;

                        case ReceiveState.CompletePayload:
                            if (VerifyPayload() == true)
                            {
                                try
                                {
                                    bool fReply = (_messageBase.Header.Flags & Flags.c_Reply) != 0;

                                    if ((_messageBase.Header.Flags & Flags.c_NACK) != 0)
                                    {
                                        _messageRaw.Payload = null;
                                    }

                                    if (await ProcessMessage(GetCompleteMessage(), fReply, cancellationToken))
                                    {
                                        DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                                        return GetCompleteMessage();
                                    }
                                    else
                                    {
                                        // this is not the message we were waiting
                                        // no need to wait for execution just throw it
                                        _parent.App.ProcessMessageAsync(GetCompleteMessage(), fReply).GetAwaiter();
                                    }

                                    _state = ReceiveState.Initialize;
                                    DebuggerEventSource.Log.WireProtocolReceiveState(_state);
                                    break;
                                }
                                catch (Exception e)
                                {
                                    Debug.WriteLine("Fault at payload deserialization:\n\n{0}", e.ToString());
                                }
                            }

                            _state = ReceiveState.Initialize;
                            DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                            if ((_messageBase.Header.Flags & Flags.c_NonCritical) == 0)
                            {
                                // FIXME 
                                // evaluate the purpose of this reply back to the nanoFramework device, the nanoCLR doesn't seem to have to handle this. In the end it looks like this does have any real purpose and will only be wasting CPU.
                                await IncomingMessage.ReplyBadPacketAsync(_parent, Flags.c_BadPayload, cancellationToken);
                                return GetCompleteMessage();
                            }

                            break;
                    }
                }
            }
            catch(Exception ex)
            {
                _state = ReceiveState.Initialize;
                DebuggerEventSource.Log.WireProtocolReceiveState(_state);
                Debug.WriteLine($"*** EXCEPTION IN STATE MACHINE***:\r\n{ex.Message} \r\n{ex.StackTrace}");
                throw;
            }
        }

        private int ValidMarker(byte[] marker)
        {
            System.Diagnostics.Debug.Assert(marker != null && marker.Length == Packet.SIZE_OF_MARKER);
            int markerSize = Packet.SIZE_OF_MARKER;
            int iMax = System.Math.Min(_rawPos, markerSize);

            for (int i = 0; i < iMax; i++)
            {
                if (_messageRaw.Header[i] != marker[i]) return -1;
            }

            if (_rawPos < markerSize) return 0;

            return 1;
        }

        private bool VerifyHeader()
        {
            uint crc = _messageBase.Header.CrcHeader;
            bool fRes;

            _messageBase.Header.CrcHeader = 0;

            fRes = CRC.ComputeCRC(_parent.CreateConverter().Serialize(_messageBase.Header), 0) == crc;

            _messageBase.Header.CrcHeader = crc;

            return fRes;
        }

        private bool VerifyPayload()
        {
            if (_messageRaw.Payload == null)
            {
                return (_messageBase.Header.Size == 0);
            }
            else
            {
                if (_messageBase.Header.Size != _messageRaw.Payload.Length) return false;

                return CRC.ComputeCRC(_messageRaw.Payload, 0) == _messageBase.Header.CrcData;
            }
        }

        public async Task<bool> ProcessMessage(IncomingMessage msg, bool fReply, CancellationToken cancellationToken)
        {
            msg.Payload = Commands.ResolveCommandToPayload(msg.Header.Cmd, fReply, _parent.Capabilities);

            if (request != null || fReply == true)
            {
                // we are processing a message flagged as a reply
                // OR we this is a reply to an explicit request

                Request reply = null;

                if (request.MatchesReply(msg))
                {
                    reply = request;

                    // FIXME: check if this return can happen here without the QueueNotify call bellow
                    return true;
                }

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

                switch (bp.Cmd)
                {
                    case Commands.c_Monitor_Ping:
                        {
                            Commands.Monitor_Ping.Reply cmdReply = new Commands.Monitor_Ping.Reply();

                            cmdReply.m_source = Commands.Monitor_Ping.c_Ping_Source_Host;
                            
                            // FIXME
                            //cmdReply.m_dbg_flags = (m_stopDebuggerOnConnect ? Commands.Monitor_Ping.c_Ping_DbgFlag_Stop : 0);

                            await msg.ReplyAsync(_parent.CreateConverter(), Flags.c_NonCritical, cmdReply, cancellationToken).ConfigureAwait(false);

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
