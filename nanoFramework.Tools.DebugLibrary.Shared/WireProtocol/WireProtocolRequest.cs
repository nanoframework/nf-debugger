//
// Copyright (c) .NET Foundation and Contributors
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
        private readonly CommandEventHandler _callback;
        private readonly int _timeout;

        public CancellationToken CancellationToken { get; }
        public TaskCompletionSource<IncomingMessage> TaskCompletionSource { get; }

        public DateTimeOffset Expires { get; private set; } = DateTimeOffset.MaxValue;

        public DateTime RequestTimestamp { get; private set; }

        public OutgoingMessage OutgoingMessage { get; }

        public bool NeedsReply => OutgoingMessage.NeedsReply;

        public WireProtocolRequest(OutgoingMessage outgoingMessage, int millisecondsTimeout = 5000, CommandEventHandler callback = null)
        {
            OutgoingMessage = outgoingMessage;
            _callback = callback;

            _timeout = millisecondsTimeout;

            // https://blogs.msdn.microsoft.com/pfxteam/2009/06/02/the-nature-of-taskcompletionsourcetresult/
            TaskCompletionSource = new TaskCompletionSource<IncomingMessage>();
        }

        internal bool PerformRequest(IController controller)
        {
            // wait for controller to be idle
            while(!controller.IsIdle())
            {
                Thread.Sleep(10);
            }

            Debug.WriteLine($"Performing request");

            DebuggerEventSource.Log.WireProtocolTxHeader(OutgoingMessage.Base.Header.CrcHeader
                                            , OutgoingMessage.Base.Header.CrcData
                                            , OutgoingMessage.Base.Header.Cmd
                                            , OutgoingMessage.Base.Header.Flags
                                            , OutgoingMessage.Base.Header.Seq
                                            , OutgoingMessage.Base.Header.SeqReply
                                            , OutgoingMessage.Base.Header.Size);

            if(controller.Send(OutgoingMessage.Raw))
            {
                // set TTL for the request
                Expires = DateTime.UtcNow.AddMilliseconds(_timeout);

                // store start time
                RequestTimestamp = DateTime.Now;

                return true;
            }

            return false;
        }

        internal void RequestAborted()
        {
            DebuggerEventSource.Log.WireProtocolTimeout(
                OutgoingMessage.Base.Header.Cmd,
                OutgoingMessage.Base.Header.Seq,
                OutgoingMessage.Base.Header.SeqReply,
                RequestTimestamp);

        }
    }
}
