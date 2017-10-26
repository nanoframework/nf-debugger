//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools;
using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class Controller : IControllerLocal
    {
        //internal byte[] marker_Debugger = Encoding.UTF8.GetBytes(Packet.MARKER_DEBUGGER_V1);
        //internal byte[] marker_Packet = Encoding.UTF8.GetBytes(Packet.MARKER_PACKET_V1);

        private string marker;
        public IControllerHostLocal App { get; internal set; }

        private int lastOutboundMessage;

        public CLRCapabilities Capabilities { get; set; }

        public Converter CreateConverter()
        {
            return new Converter(Capabilities);
        }

        private int nextEndpointId;

        //private FifoBuffer m_inboundData;

        //private Thread m_inboundDataThread;

        //private Thread m_stateMachineThread;

        //private ManualResetEvent m_evtShutdown;

        //private State m_state;
        //private CLRCapabilities capabilities;
        //private WaitHandle[] m_waitHandlesRead;

        public Controller(string marker, IControllerHostLocal app)
        {
            this.marker = marker;
            App = app;

            Random random = new Random();

            lastOutboundMessage = random.Next(65536);
            nextEndpointId = random.Next(int.MaxValue);
            //m_state = new State(this);

            //default capabilities
            Capabilities = new CLRCapabilities();
        }

        internal void SetMarker(Packet bp, string sig)
        {
            byte[] buf = Encoding.UTF8.GetBytes(sig);

            Array.Copy(buf, 0, bp.Marker, 0, buf.Length);
        }

        public async Task<bool> QueueOutputAsync(MessageRaw raw, CancellationToken cancellationToken)
        {
            // TX header
            var sendHeaderCount = await SendRawBufferAsync(raw.Header, TimeSpan.FromMilliseconds(1000), cancellationToken);

            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                return false;
            }

            if (raw.Payload != null)
            {
                // we have a payload to TX
                if (sendHeaderCount == raw.Header.Length)
                {
                    var sendPayloadCount = await SendRawBufferAsync(raw.Payload, TimeSpan.FromMilliseconds(1000), cancellationToken);

                    if (sendPayloadCount == raw.Payload.Length)
                    {
                        // payload TX OK
                        return true;
                    }
                    else
                    {
                        // failed TX the payload
                        return false;
                    }
                }
                else
                {
                    // already failed to TX header so don't bother with the payload
                    return false;
                }
            }
            else
            {
                // no payload, header TX OK, we are good
                return true;
            }
        }

        public Packet NewPacket()
        {
            //if (!m_state.IsRunning)
            //    throw new ArgumentException("Controller not started, cannot create message");

            Packet bp = new Packet();

            SetMarker(bp, marker);

            bp.Seq = (ushort)Interlocked.Increment(ref lastOutboundMessage);

            return bp;
        }

        public void StopProcessing()
        {
            throw new NotImplementedException();
        }

        public void ResumeProcessing()
        {
            throw new NotImplementedException();
        }

        public uint GetUniqueEndpointId()
        {
            throw new NotImplementedException();
        }

        public Task<uint> SendRawBufferAsync(byte[] buffer, TimeSpan waiTimeout, CancellationToken cancellationToken)
        {
            return App.SendBufferAsync(buffer, waiTimeout, cancellationToken);
        }

        internal async Task<int> ReadBufferAsync(byte[] buffer, int offset, int bytesToRead, TimeSpan waitTimeout, CancellationToken cancellationToken)
        {
            //
            int bytesToReadRequested = bytesToRead;

            // sanity check for anything to read
            if (bytesToRead == 0)
            {
                return 0;
            }

            while (bytesToRead > 0)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    // cancellation requested
                    break;
                }

                // read next chunk of data async
                var readResult = await App.ReadBufferAsync((uint)bytesToRead, waitTimeout, cancellationToken);

                // any byte read?
                if (readResult.Length > 0)
                {
                    Array.Copy(readResult, 0, buffer, offset, readResult.Length);

                    offset += readResult.Length;
                    bytesToRead -= readResult.Length;
                }
            }

            return bytesToReadRequested - bytesToRead;
        }
    }
}
