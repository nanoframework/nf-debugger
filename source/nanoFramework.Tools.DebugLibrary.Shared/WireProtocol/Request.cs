//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger
{
    public delegate void CommandEventHandler(IncomingMessage msg, bool fReply);

    internal class Request
    {
        internal Controller ctrl;
        internal OutgoingMessage outgoingMsg;
        internal IncomingMessage responseMsg;
        internal int retries;
        internal TimeSpan waitRetryTimeout;
        internal TimeSpan totalWaitTimeout;
        internal CommandEventHandler callback;
        internal ManualResetEvent m_event;
        internal Timer timer;

        internal Request(Controller ctrl, OutgoingMessage outMsg, int retries, int timeout, CommandEventHandler callback)
        {
            if (retries < 0)
            {
                throw new ArgumentException("Value cannot be negative", "retries");
            }

            if (timeout < 1 || timeout > 60 * 60 * 1000)
            {
                throw new ArgumentException(String.Format("Value out of bounds: {0}", timeout), "timeout");
            }

            //this.parent = parent;
            this.ctrl = ctrl;
            outgoingMsg = outMsg;
            this.retries = retries;
            waitRetryTimeout = new TimeSpan(timeout * TimeSpan.TicksPerMillisecond);
            totalWaitTimeout = new TimeSpan((retries == 0 ? 1 : 2 * retries) * timeout * TimeSpan.TicksPerMillisecond);
            this.callback = callback;

            if (callback == null)
                m_event = new ManualResetEvent(false);
        }

        internal bool MatchesReply(IncomingMessage res)
        {
            Packet headerReq = outgoingMsg.Header;
            Packet headerRes = res.Header;

            if (headerReq.m_cmd == headerRes.m_cmd &&
               headerReq.m_seq == headerRes.m_seqReply)
            {
                return true;
            }

            return false;
        }

        internal void Signal(IncomingMessage res)
        {
            lock (this)
            {
                if (timer != null)
                {
                    timer.Dispose();
                    timer = null;
                }

                responseMsg = res;
            }

            Signal();
        }

        internal void Signal()
        {
            CommandEventHandler callback;
            IncomingMessage res;

            lock (this)
            {
                callback = this.callback;
                res = responseMsg;

                if (timer != null)
                {
                    timer.Dispose();
                    timer = null;
                }

                if (m_event != null)
                {
                    m_event.Set();
                }
            }

            if (callback != null)
            {
                callback(res, true);
            }
        }

        internal async Task<IncomingMessage> PerformRequestAsync(CancellationToken cancellationToken)
        {
            int retryCounter = 0;

            IncomingMessage reply = null;
            var reassembler = new MessageReassembler(ctrl, this);

            //Debug.WriteLine("timeout is " + waitRetryTimeout.TotalMilliseconds + "ms");

            while (retryCounter++ <= retries)
            {
                // send message
                if (await outgoingMsg.SendAsync().ConfigureAwait(false))
                {
                    // if this request is for a reboot, we won't be able to receive the reply right away because the device is rebooting
                    if((outgoingMsg.Header.m_cmd == Commands.c_Monitor_Reboot) && retryCounter == 1)
                    {
                        // wait here for 
                        Task.Delay(2000).Wait();
                    }

                    CancellationTokenSource source = new CancellationTokenSource();
                    // add a cancellation token to force cancel
                    var timeoutCancelatioToken = source.Token.AddTimeout(waitRetryTimeout);

                    // because we have an external cancellation token and the above timeout cancellation token, need to combine both
                    var linkedCancelationToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancelatioToken).Token;
                    
                    try
                    {
                        // need to have a timeout to cancel the process task otherwise it may end up waiting forever for this to return
                        // because we have an external cancellation token and the above timeout cancellation token, need to combine both
                        reply = await reassembler.ProcessAsync(linkedCancelationToken).ConfigureAwait(false);
                    }
                    catch { }
                    finally
                    {
                        // ALWAYS cancel reassembler task
                        source.Cancel();
                    }

                    if (reply != null)
                    {
                        return reply;
                    }
                }
                else
                {
                    // send failed
                    //Debug.WriteLine("SEND FAILED...");
                }

                // something went wrong, retry with a progressive back-off strategy
                Task.Delay(200 * retryCounter).Wait();
            }

            //Debug.WriteLine("exceeded attempts count...");

            return null;
        }
    }
}
