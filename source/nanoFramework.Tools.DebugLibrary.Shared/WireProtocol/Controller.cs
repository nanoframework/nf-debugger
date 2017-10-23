﻿//
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
            Debug.WriteLine("QueueOutputAsync 1");
            await SendRawBufferAsync(raw.Header, TimeSpan.FromMilliseconds(1000), cancellationToken).ConfigureAwait(false);

            // check for cancelation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancelation requested");
                return false;
            }

            if (raw.Payload != null)
            {
                Debug.WriteLine("QueueOutputAsync 2");

                await SendRawBufferAsync(raw.Payload, TimeSpan.FromMilliseconds(1000), cancellationToken).ConfigureAwait(false);
            }

            return true;
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

        public async Task<uint> SendRawBufferAsync(byte[] buffer, TimeSpan waiTimeout, CancellationToken cancellationToken)
        {
            Debug.WriteLine("SendRawBufferAsync");

            return await App.SendBufferAsync(buffer, waiTimeout, cancellationToken).ConfigureAwait(false);
        }

        internal async Task<int> ReadBufferAsync(byte[] buffer, int offset, int bytesToRead, TimeSpan waitTimeout, CancellationToken cancellationToken)
        {
            //
            int bytesToReadRequested = bytesToRead;

            // sanity check for anything to read
            if(bytesToRead == 0)
            {
                //Debug.WriteLine("Nothing to read, leaving now");
                return 0;
            }

            Debug.WriteLine("Trying to read {0} bytes...", bytesToReadRequested);

            while (bytesToRead > 0)
            {
                if(cancellationToken.IsCancellationRequested)
                {
                    // cancellation requested
                    break;
                }

                // read next chunk of data async
                var readResult = await App.ReadBufferAsync((uint)bytesToRead, waitTimeout, cancellationToken).ConfigureAwait(false);

                Debug.WriteLine("read {0} bytes", readResult.Length);
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
