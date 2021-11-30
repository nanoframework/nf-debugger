//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.Text;
using System.Threading;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class MessageReassembler
    {
        public enum ReceiveState
        {
            Idle,
            ExitingIdle,
            Initialize,
            WaitingForHeader,
            ReadingHeader,
            CompleteHeader,
            ReadingPayload,
            CompletePayload,
        }

        // constants to use in the inactivity back-off calculation
        private const int MaxBackoffTime = 500;
        private const int BackoffTimeStep1 = (int)(MaxBackoffTime * 0.80);
        private const int BackoffTimeStep2 = (int)(MaxBackoffTime * 0.40);
        private const int BackoffTimeStep3 = (int)(MaxBackoffTime * 0.10);
        private const int BackoffTimeStep4 = (int)(MaxBackoffTime * 0.05);

        // constants to use the inactivity calculation
        private const int MaxInactivityTime = 5000;
        private const int MaxInactivityTimeStep1 = (int)(MaxInactivityTime * 0.75);
        private const int MaxInactivityTimeStep2 = (int)(MaxInactivityTime * 0.25);
        private const int MaxInactivityTimeStep3 = (int)(MaxInactivityTime * 0.05);
        private const int MaxInactivityTimeStep4 = (int)(MaxInactivityTime * 0.01);

        private readonly byte[] _markerDebugger = Encoding.UTF8.GetBytes(Packet.MARKER_DEBUGGER_V1);
        private readonly byte[] _markerPacket = Encoding.UTF8.GetBytes(Packet.MARKER_PACKET_V1);

        private readonly Controller _parent;
        private ReceiveState _state;
        private ReceiveState _previousState;

        private MessageRaw _messageRaw;
        private int _rawPos;
        private MessageBase _messageBase;

        /// <summary>
        /// Timeout for the ongoing operation.
        /// </summary>
        private DateTime _messageEventTimeout;

        private DateTime _lastInactivityReport;
        private TimeSpan _inactivityTime;

        public MessageReassembler(Controller parent)
        {
            _parent = parent;
            _state = ReceiveState.Initialize;
            _parent.LastActivity = DateTime.UtcNow;
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
            int sleepTime = 0;

            _inactivityTime = (DateTime.UtcNow - _parent.LastActivity);

            // activity check, if not exiting idle state
            if (_state != ReceiveState.ExitingIdle)
            {
                // progressive back-off 
                if (_inactivityTime.TotalMilliseconds >= MaxInactivityTime)
                {
                    sleepTime = MaxBackoffTime;
                }
                else if (_inactivityTime.TotalMilliseconds >= MaxInactivityTimeStep1)
                {
                    sleepTime = BackoffTimeStep1;
                }
                else if (_inactivityTime.TotalMilliseconds >= MaxInactivityTimeStep2)
                {
                    sleepTime = BackoffTimeStep2;
                }
                else if (_inactivityTime.TotalMilliseconds >= MaxInactivityTimeStep3)
                {
                    sleepTime = BackoffTimeStep3;
                }
                else if (_inactivityTime.TotalMilliseconds >= MaxInactivityTimeStep4)
                {
                    sleepTime = BackoffTimeStep4;
                }

                if (sleepTime > 0)
                {
                    _previousState = _state;
                    _state = ReceiveState.Idle;

                    if (_lastInactivityReport == DateTime.MinValue)
                    {
                        _lastInactivityReport = DateTime.UtcNow.AddSeconds(10);
                    }
                }
            }
            else
            {
                // restore previous state
                _state = _previousState;
            }

            try
            {
                switch (_state)
                {
                    case ReceiveState.Idle:

                        if (DateTime.UtcNow > _lastInactivityReport)
                        {
                            DebuggerEventSource.Log.WireProtocolReceiveState(_state, _inactivityTime);

                            // reset 
                            _lastInactivityReport = DateTime.MinValue;
                        }

                        // sleep now
                        Thread.Sleep(sleepTime);

                        // restore previous state
                        _state = ReceiveState.ExitingIdle;

                        break;

                    case ReceiveState.Initialize:

                        DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                        _rawPos = 0;

                        _messageBase = new MessageBase
                        {
                            Header = new Packet()
                        };

                        _messageRaw = new MessageRaw
                        {
                            Header = _parent.CreateConverter().Serialize(_messageBase.Header)
                        };

                        // reset event timeout
                        _messageEventTimeout = DateTime.MinValue;

                        _state = ReceiveState.WaitingForHeader;

                        DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                        goto case ReceiveState.WaitingForHeader;

                    case ReceiveState.WaitingForHeader:

                        // try to read marker
                        count = Packet.SIZE_OF_MARKER - _rawPos;

                        bytesRead = _parent.ReadBuffer(_messageRaw.Header, _rawPos, count);

                        _rawPos += bytesRead;

                        // activity check
                        if (bytesRead > 0)
                        {
                            _parent.LastActivity = DateTime.UtcNow;
                        }

                        // loop trying to find the markers in the stream
                        while (_rawPos > 0)
                        {
                            var debuggerMarkerPresence = ValidMarker(_markerDebugger);
                            var packetMarkerPresence = ValidMarker(_markerPacket);

                            if (debuggerMarkerPresence == MarkerPresence.Present
                                || packetMarkerPresence == MarkerPresence.Present)
                            {
                                _state = ReceiveState.ReadingHeader;
                                
                                DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                                goto case ReceiveState.ReadingHeader;
                            }

                            if (debuggerMarkerPresence == MarkerPresence.PresentButOutOfSync
                                || packetMarkerPresence == MarkerPresence.PresentButOutOfSync)
                            {
                                break; 
                            }

                            _parent.App.SpuriousCharacters(_messageRaw.Header, 0, 1);

                            Array.Copy(_messageRaw.Header, 1, _messageRaw.Header, 0, --_rawPos);
                        }

                        break;

                    case ReceiveState.ReadingHeader:
                        count = _messageRaw.Header.Length - _rawPos;

                        // check timeout
                        if(_messageEventTimeout == DateTime.MinValue)
                        {
                            _messageEventTimeout = DateTime.UtcNow.AddSeconds(5);
                        }
                        else
                        {
                            if(DateTime.UtcNow > _messageEventTimeout )
                            {
                                // quit receiving this packet, abort
                                _state = ReceiveState.Initialize;

                                DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                                Debug.WriteLine("*** TIMEOUT ERROR waiting for header. Initializing.");

                                break;
                            }
                        }

                        bytesRead = _parent.ReadBuffer(_messageRaw.Header, _rawPos, count);

                        _rawPos += bytesRead;

                        // activity check
                        if (bytesRead > 0)
                        {
                            _parent.LastActivity = DateTime.UtcNow;
                        }

                        if (bytesRead != count)
                        {
                            break;
                        }

                        _state = ReceiveState.CompleteHeader;

                        DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                        goto case ReceiveState.CompleteHeader;

                    case ReceiveState.CompleteHeader:
                        try
                        {
                            _parent.CreateConverter().Deserialize(_messageBase.Header, _messageRaw.Header);

                            var request = ((Engine)(_parent.App)).FindRequest(_messageBase.Header);

                            DebuggerEventSource.Log.WireProtocolRxHeader(
                                _messageBase.Header.CrcHeader,
                                _messageBase.Header.CrcData,
                                _messageBase.Header.Cmd,
                                _messageBase.Header.Flags,
                                _messageBase.Header.Seq,
                                _messageBase.Header.SeqReply,
                                _messageBase.Header.Size,
                                request != null ? request.RequestTimestamp : DateTime.MinValue);

                            

                            if (VerifyHeader())
                            {
                                if (_messageBase.Header.Size != 0)
                                {
                                    // sanity check for wrong size (can happen with CRC32 turned off)
                                    if(_messageBase.Header.Size > 2048)
                                    {
                                        Debug.WriteLine("Invalid payload requested. Initializing.");

                                        _state = ReceiveState.Initialize;

                                        DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                                        break;
                                    }

                                    // setup buffer to read payload
                                    _messageRaw.Payload = new byte[_messageBase.Header.Size];

                                    // reset timeout
                                    _messageEventTimeout = DateTime.MinValue;

                                    //reuse _rawPos for position in header for reading
                                    _rawPos = 0;

                                    _state = ReceiveState.ReadingPayload;

                                    DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                                    goto case ReceiveState.ReadingPayload;
                                }
                                else
                                {
                                    _state = ReceiveState.CompletePayload;

                                    DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                                    goto case ReceiveState.CompletePayload;
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

                        DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                        if ((_messageBase.Header.Flags & Flags.c_NonCritical) == 0)
                        {
                            _parent.App.ReplyBadPacket(Flags.c_BadHeader);
                        }

                        break;

                    case ReceiveState.ReadingPayload:
                        count = _messageRaw.Payload.Length - _rawPos;

                        // check timeout
                        if (_messageEventTimeout == DateTime.MinValue)
                        {
                            _messageEventTimeout = DateTime.UtcNow.AddMilliseconds(500);
                        }
                        else
                        {
                            if (DateTime.UtcNow > _messageEventTimeout)
                            {
                                Debug.WriteLine($"*** TIMEOUT ERROR waiting for payload. Missing {count}/{_messageRaw.Payload.Length}. Initializing.");

                                // quit receiving this packet, abort
                                _state = ReceiveState.Initialize;

                                DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                                break;
                            }
                        }

                        bytesRead = _parent.ReadBuffer(_messageRaw.Payload, _rawPos, count);

                        _rawPos += bytesRead;

                        // activity check
                        if (bytesRead > 0)
                        {
                            _parent.LastActivity = DateTime.UtcNow;
                        }

                        if (bytesRead != count)
                        {
                            break;
                        }

                        _state = ReceiveState.CompletePayload;

                        DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                        goto case ReceiveState.CompletePayload;

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

                                DebuggerEventSource.Log.OutputPayload(_messageRaw.Payload);

                                _parent.App.ProcessMessage(GetCompleteMessage(), fReply);

                                // setup restart
                                _state = ReceiveState.Initialize;

                                DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                                return;
                            }
                            catch (AggregateException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"Fault at payload de-serialization:\n\n{ex.Message}");
                            }
                        }

                        _state = ReceiveState.Initialize;

                        DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                        if ((_messageBase.Header.Flags & Flags.c_NonCritical) == 0)
                        {
                            _parent.App.ReplyBadPacket(Flags.c_BadPayload);
                        }

                        break;
                }
            }
            catch
            {
                _state = ReceiveState.Initialize;
                
                DebuggerEventSource.Log.WireProtocolReceiveState(_state);

                throw;
            }
        }

        private MarkerPresence ValidMarker(byte[] marker)
        {
            if (marker is null)
            {
                throw new ArgumentNullException(nameof(marker));
            }

            Debug.Assert(marker.Length == Packet.SIZE_OF_MARKER);
            
            int markerSize = Packet.SIZE_OF_MARKER;
            int iMax = Math.Min(_rawPos, markerSize);

            for (int i = 0; i < iMax; i++)
            {
                if (_messageRaw.Header[i] != marker[i])
                {
                    return MarkerPresence.NotPresent;
                }
            }

            if (_rawPos < markerSize)
            {
                return MarkerPresence.PresentButOutOfSync;
            }

            return MarkerPresence.Present;
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

        private enum MarkerPresence
        {
            /// <summary>
            /// Marker is not present.
            /// </summary>
            NotPresent = -1,

            /// <summary>
            /// Marker is present but not at the expected position.
            /// </summary>
            PresentButOutOfSync = 0,

            /// <summary>
            /// Marker is present at the expected position.
            /// </summary>
            Present = 1
        }
    }
}
