//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class MessageReassembler
    {
        public enum ReceiveState
        {
            Idle = 0,
            Initialize = 1,
            WaitingForHeader = 2,
            ReadingHeader = 3,
            CompleteHeader = 4,
            ReadingPayload = 5,
            CompletePayload = 6,
        }

        private readonly byte[] _markerDebugger = Encoding.UTF8.GetBytes(Packet.MARKER_DEBUGGER_V1);
        private readonly byte[] _markerPacket = Encoding.UTF8.GetBytes(Packet.MARKER_PACKET_V1);

        private readonly Controller _parent;
        private ReceiveState _state;

        private MessageRaw _messageRaw;
        private int _rawPos;
        private MessageBase _messageBase;

        public MessageReassembler(Controller parent)
        {
            _parent = parent;
            _state = ReceiveState.Initialize;
        }

        public IncomingMessage GetCompleteMessage()
        {
            return new IncomingMessage(_parent, _messageRaw, _messageBase);
        }

        /// <summary>
        /// Essential Rx method. Drives state machine by reading data and processing it. This works in
        /// conjunction with NotificationThreadWorker [Tx].
        /// </summary>
        public void Process()
        {
            int count;
            int bytesRead;

            try
            {
                DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                switch (_state)
                {
                    case ReceiveState.Initialize:
                        _rawPos = 0;

                        _messageBase = new MessageBase
                        {
                            Header = new Packet()
                        };

                        _messageRaw = new MessageRaw
                        {
                            Header = _parent.CreateConverter().Serialize(_messageBase.Header)
                        };

                        _state = ReceiveState.WaitingForHeader;
                        break;

                    case ReceiveState.WaitingForHeader:

                        count = _messageRaw.Header.Length - _rawPos;

                        bytesRead = _parent.ReadBuffer(_messageRaw.Header, _rawPos, count);

                        _rawPos += bytesRead;

                        // sanity check
                        if (bytesRead != _messageRaw.Header.Length)
                        {
                            // doesn't look like a header, better restart
                            _state = ReceiveState.Initialize;
                            break;
                        }

                        while (_rawPos > 0)
                        {
                            int flag_Debugger = ValidMarker(_markerDebugger);
                            int flag_Packet = ValidMarker(_markerPacket);

                            if (flag_Debugger == 1 || flag_Packet == 1)
                            {
                                _state = ReceiveState.ReadingHeader;
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

                        bytesRead = _parent.ReadBuffer(_messageRaw.Header, _rawPos, count);

                        _rawPos += bytesRead;

                        if (bytesRead != count)
                        {
                            break;
                        }

                        _state = ReceiveState.CompleteHeader;
                        break;

                    case ReceiveState.CompleteHeader:
                        try
                        {
                            _parent.CreateConverter().Deserialize(_messageBase.Header, _messageRaw.Header);

                            if (VerifyHeader())
                            {
                                DebuggerEventSource.Log.WireProtocolRxHeader(_messageBase.Header.CrcHeader, _messageBase.Header.CrcData, _messageBase.Header.Cmd, _messageBase.Header.Flags, _messageBase.Header.Seq, _messageBase.Header.SeqReply, _messageBase.Header.Size);

                                if (_messageBase.Header.Size != 0)
                                {
                                    // sanity check for wrong size (can happen with CRC32 turned off)
                                    if(_messageBase.Header.Size > 2048)
                                    {
                                        Debug.WriteLine("Invalid payload requested. Initializing.");

                                        _state = ReceiveState.Initialize;

                                        break;
                                    }
                                    _messageRaw.Payload = new byte[_messageBase.Header.Size];
                                    //reuse m_rawPos for position in header to read.
                                    _rawPos = 0;

                                    _state = ReceiveState.ReadingPayload;
                                    break;
                                }
                                else
                                {
                                    _state = ReceiveState.CompletePayload;
                                    break;
                                }
                            }
                        }
                        catch (AggregateException)
                        {
                            throw;
                        }
                        catch (Exception e)
                        {
                            Debug.WriteLine("Fault at payload de-serialization:\n\n{0}", e.ToString());
                        }

                        _state = ReceiveState.Initialize;
                        break;

                    case ReceiveState.ReadingPayload:
                        count = _messageRaw.Payload.Length - _rawPos;

                        bytesRead = _parent.ReadBuffer(_messageRaw.Payload, _rawPos, count);

                        _rawPos += bytesRead;

                        if (bytesRead != count) break;

                        _state = ReceiveState.CompletePayload;

                        break;

                    case ReceiveState.CompletePayload:
                        if (VerifyPayload())
                        {
                            try
                            {
                                bool fReply = (_messageBase.Header.Flags & Flags.c_Reply) != 0;

                                if ((_messageBase.Header.Flags & Flags.c_NACK) != 0)
                                {
                                    _messageRaw.Payload = null;
                                }

                                _parent.App.ProcessMessage(GetCompleteMessage(), fReply);

                                // setup restart
                                _state = ReceiveState.Initialize;
                                return;
                            }
                            catch (AggregateException)
                            {
                                throw;
                            }
                            catch (Exception e)
                            {
                                Debug.WriteLine("Fault at payload de-serialization:\n\n{0}", e.ToString());
                            }
                        }

                        _state = ReceiveState.Initialize;
                        break;
                }
            }
            catch
            {
                _state = ReceiveState.Initialize;
                Debug.WriteLine("*** EXCEPTION IN STATE MACHINE***");
                throw;
            }
        }

        private int ValidMarker(byte[] marker)
        {
            Debug.Assert(marker != null && marker.Length == Packet.SIZE_OF_MARKER);
            int markerSize = Packet.SIZE_OF_MARKER;
            int iMax = Math.Min(_rawPos, markerSize);

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


            // verify CRC32 only if connected device has reported that it implements it
            if (_parent.App.IsCRC32EnabledForWireProtocol)
            {
                // compute CRC32 with header CRC32 zeroed
                _messageBase.Header.CrcHeader = 0;

                fRes = CRC.ComputeCRC(_parent.CreateConverter().Serialize(_messageBase.Header), 0) == crc;

                _messageBase.Header.CrcHeader = crc;
            }
            else
            {
                fRes = true;
            }

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

                // verify CRC32 only if connected device has reported that it implements it
                if (_parent.App.IsCRC32EnabledForWireProtocol)
                {
                    return CRC.ComputeCRC(_messageRaw.Payload, 0) == _messageBase.Header.CrcData;
                }
                else
                {
                    return true;
                }
            }
        }
    }
}
