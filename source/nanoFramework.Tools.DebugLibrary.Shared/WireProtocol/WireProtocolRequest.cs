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

        public CancellationToken CancellationToken { get; }
        public TaskCompletionSource<IncomingMessage> TaskCompletionSource { get; }

        public DateTimeOffset Expires { get; }
        public OutgoingMessage OutgoingMessage { get; }

        public WireProtocolRequest(OutgoingMessage outgoingMessage, CancellationToken cancellationToken, int millisecondsTimeout = 5000, CommandEventHandler callback = null)
        {
            OutgoingMessage = outgoingMessage;
            _callback = callback;

            // set TTL for the request
            Expires = DateTime.Now.AddMilliseconds(millisecondsTimeout);

            // https://blogs.msdn.microsoft.com/pfxteam/2009/06/02/the-nature-of-taskcompletionsourcetresult/
            TaskCompletionSource = new TaskCompletionSource<IncomingMessage>();
            CancellationToken = cancellationToken;
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
