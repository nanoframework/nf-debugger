//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the Microsoft .NET Micro Framework and is unsupported. 
// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use these files except in compliance with the License.
// You may obtain a copy of the License at:
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing
// permissions and limitations under the License.
// 
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using Microsoft.NetMicroFramework.Tools;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Microsoft.SPOT.Debugger.WireProtocol
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

        internal void SetSignature(Packet bp, string sig)
        {
            byte[] buf = Encoding.UTF8.GetBytes(sig);

            Array.Copy(buf, 0, bp.m_signature, 0, buf.Length);
        }

        public async Task<bool> QueueOutputAsync(MessageRaw raw)
        {
            await SendRawBufferAsync(raw.m_header, TimeSpan.FromMilliseconds(1000), new CancellationToken()).ConfigureAwait(false);

            if (raw.m_payload != null)
            {
                await SendRawBufferAsync(raw.m_payload, TimeSpan.FromMilliseconds(1000), new CancellationTokenSource().Token).ConfigureAwait(false);
            }

            return true;
        }

        public Packet NewPacket()
        {
            //if (!m_state.IsRunning)
            //    throw new ArgumentException("Controller not started, cannot create message");

            Packet bp = new Packet();

            SetSignature(bp, marker);

            bp.m_seq = (ushort)Interlocked.Increment(ref lastOutboundMessage);

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

            //Debug.WriteLine("Trying to read {0} bytes...", bytesToReadRequested);

            while (bytesToRead > 0)
            {
                if(cancellationToken.IsCancellationRequested)
                {
                    // cancellation requested
                    break;
                }

                // read next chunk of data async
                var readResult = await App.ReadBufferAsync((uint)bytesToRead, waitTimeout, cancellationToken).ConfigureAwait(false);

                //Debug.WriteLine("read {0} bytes", readResult.UnconsumedBufferLength);

                // any byte read?
                if (readResult.UnconsumedBufferLength > 0)
                {
                    byte[] readBuffer = new byte[readResult.UnconsumedBufferLength];
                    readResult.ReadBytes(readBuffer);

                    Array.Copy(readBuffer, 0, buffer, offset, readBuffer.Length);

                    offset += readBuffer.Length;
                    bytesToRead -= readBuffer.Length;
                }
            }

            return bytesToReadRequested - bytesToRead;
        }
    }
}
