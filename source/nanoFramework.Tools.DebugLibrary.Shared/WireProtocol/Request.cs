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
    internal class Request
    {
        internal Controller ctrl;
        internal OutgoingMessage outgoingMsg;
        internal IncomingMessage responseMsg;
        internal TimeSpan timeout;
        internal CommandEventHandler callback;
        internal ManualResetEvent m_event;
        internal Timer timer;

        internal Request(Controller ctrl, OutgoingMessage outMsg, int timeout, CommandEventHandler callback)
        {
            if (timeout < 1 || timeout > 60 * 60 * 1000)
            {
                throw new ArgumentException(String.Format("Value out of bounds: {0}", timeout), "timeout");
            }

            //this.parent = parent;
            this.ctrl = ctrl;
            outgoingMsg = outMsg;
            this.timeout = new TimeSpan(timeout * TimeSpan.TicksPerMillisecond);
            this.callback = callback;

            if (callback == null)
            {
                m_event = new ManualResetEvent(false);
            }
        }

        internal async Task SendAsync(CancellationToken cancellationToken)
        {
            await outgoingMsg.SendAsync(cancellationToken);
        }

        internal bool MatchesReply(IncomingMessage res)
        {
            Packet headerReq = outgoingMsg.Header;
            Packet headerRes = res.Header;

            if (headerReq.Cmd == headerRes.Cmd &&
               headerReq.Seq == headerRes.SeqReply)
            {
                return true;
            }

            return false;
        }

        // TODO
        // need to evaluate this method
        internal async Task<IncomingMessage> WaitAsync()
        {
            if (m_event == null)
                return responseMsg;

            var waitStartTime = DateTime.UtcNow;
            var requestTimedOut = !m_event.WaitOne(timeout);

            // Wait for m_waitRetryTimeout milliseconds, if we did not get a signal by then
            // attempt sending the request again, and then wait more.
            while (requestTimedOut)
            {
                var deltaT = DateTime.UtcNow - waitStartTime;
                if (deltaT >= timeout)
                    break;

                //if (retries <= 0)
                //    break;

                if (await outgoingMsg.SendAsync(new CancellationToken()))
                {
                    //retries--;
                }

                requestTimedOut = !m_event.WaitOne(timeout);
            }

            if (requestTimedOut)
            {

                // FIXME ctrl.CancelRequest(this);
            }

            // FIXME
            //if (responseMsg == null && m_parent.ThrowOnCommunicationFailure)
            //{
            //    //do we want a separate exception for aborted requests?
            //    throw new IOException("Request failed");
            //}

            return responseMsg;
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

        internal async Task<IncomingMessage> PerformRequestAsync()
        {
            IncomingMessage reply = null;

            var reassembler = new MessageReassembler(ctrl, this);

            //// TODO add cancel token argument here
            //// check for cancellation request
            //if (cancellationToken.IsCancellationRequested)
            //{
            //    // cancellation requested
            //    Debug.WriteLine("cancellation requested");
            //    return null;
            //}

            Debug.WriteLine($"Performing request");

            // create new cancellation token source
            CancellationTokenSource cTSource = new CancellationTokenSource();

            // send message
            // add a cancellation token to force cancel, the send 
            if (await outgoingMsg.SendAsync(cTSource.Token))
            {
                // if this request is for a reboot, we won't be able to receive the reply right away because the device is rebooting
                if (outgoingMsg.Header.Cmd == Commands.c_Monitor_Reboot)
                {
                    // done here, no reply will come
                    return reply;
                }

                Debug.WriteLine($"Processing reply now...");

                // ALWAYS cancel token before issuing a new one
                cTSource.Cancel();

                // create new cancellation token for reply processor
                cTSource = new CancellationTokenSource();

                try
                {
                    // need to have a timeout to cancel the process task otherwise it may end up waiting forever for this to return
                    // because we have an external cancellation token and the above timeout cancellation token, need to combine both
                    reply = await reassembler.ProcessAsync(cTSource.Token.AddTimeout(timeout));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Exception occurred: {ex.Message}\r\n {ex.StackTrace}");

                    // ALWAYS cancel reassembler task on exception
                    cTSource.Cancel();
                }
            }
            else
            {
                // send failed
                Debug.WriteLine("SEND FAILED...");
            }

            return reply;
        }
    }
}
