//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class WireProtocolRequest
    {
        private CommandEventHandler _callback;
        private ManualResetEvent _event;

        public CancellationToken CancellationToken { get; }
        public TaskCompletionSource<object> TaskCompletionSource { get; }

        public DateTimeOffset Expires { get; }
        public OutgoingMessage OutgoingMessage { get; private set; }

        internal WireProtocolRequest(OutgoingMessage outgoingMessage, CommandEventHandler callback, CancellationToken cancellationToken)
        {
            OutgoingMessage = outgoingMessage;
            _callback = callback;

            if (callback == null)
            {
                _event = new ManualResetEvent(false);
            }

            // a request has TTL of 20 seconds
            Expires = DateTime.Now.AddSeconds(20);

            // https://blogs.msdn.microsoft.com/pfxteam/2009/06/02/the-nature-of-taskcompletionsourcetresult/
            TaskCompletionSource = new TaskCompletionSource<object>();
            CancellationToken = cancellationToken;
        }

        internal bool MatchesReply(IncomingMessage res)
        {
            Packet headerRequest = OutgoingMessage.Header;
            Packet headerResponse = res.Header;

            if (headerRequest.Cmd == headerResponse.Cmd &&
               headerRequest.Seq == headerResponse.SeqReply)
            {
                return true;
            }

            return false;
        }
        
        internal async Task<bool> PerformRequestAsync(IController controller)
        {
            Debug.WriteLine($"Performing request");

            DebuggerEventSource.Log.WireProtocolTxHeader(OutgoingMessage.Base.Header.CrcHeader
                                            , OutgoingMessage.Base.Header.CrcData
                                            , OutgoingMessage.Base.Header.Cmd
                                            , OutgoingMessage.Base.Header.Flags
                                            , OutgoingMessage.Base.Header.Seq
                                            , OutgoingMessage.Base.Header.SeqReply
                                            , OutgoingMessage.Base.Header.Size
                                            );

            return await controller.SendAsync(OutgoingMessage.Raw, CancellationToken);
        }
    }
}
