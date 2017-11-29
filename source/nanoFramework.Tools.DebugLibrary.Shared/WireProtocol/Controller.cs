//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class Controller : IControllerLocal
    {
        private int lastOutboundMessage;
        private Semaphore _sendSemaphore = new Semaphore(1, 1);
        private int nextEndpointId;

        public IControllerHostLocal App { get; internal set; }

        public CLRCapabilities Capabilities { get; set; }

        public Controller(IControllerHostLocal app)
        {
            App = app;

            Random random = new Random();

            lastOutboundMessage = random.Next(65536);
            nextEndpointId = random.Next(int.MaxValue);

            //default capabilities
            Capabilities = new CLRCapabilities();
        }

        public Converter CreateConverter()
        {
            return new Converter(Capabilities);
        }

        public async Task<bool> SendAsync(MessageRaw raw, CancellationToken cancellationToken)
        {
             _sendSemaphore.WaitOne();

            try
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
            catch (TaskCanceledException)
            {
                // don't do anything here, as this is expected
            }
            finally
            {
                _sendSemaphore.Release();
            }

            return false;
        }

        public ushort GetNextSequenceId()
        {
            return (ushort)Interlocked.Increment(ref lastOutboundMessage);
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

        private Task<uint> SendRawBufferAsync(byte[] buffer, TimeSpan waiTimeout, CancellationToken cancellationToken)
        {
            return App.SendBufferAsync(buffer, waiTimeout, cancellationToken);
        }

        internal async Task<int> ReadBufferAsync(byte[] buffer, int offset, int bytesToRead, TimeSpan waitTimeout, CancellationToken cancellationToken)
        {
            int bytesToReadRequested = bytesToRead;

            try
            {
                // sanity check for anything to read
                if (bytesToRead == 0)
                {
                    return 0;
                }

                while (bytesToRead > 0 && !cancellationToken.IsCancellationRequested)
                {
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
            }
            catch (TaskCanceledException)
            {
                // don't do anything here, as this is expected
            }

            return bytesToReadRequested - bytesToRead;
        }
    }
}
