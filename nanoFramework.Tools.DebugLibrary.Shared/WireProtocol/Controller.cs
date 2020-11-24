//
// Copyright (c) .NET Foundation and Contributors
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
        private ushort lastOutboundMessage;
        private readonly Semaphore _sendSemaphore = new Semaphore(1, 1);
        private readonly Semaphore _receiveSemaphore = new Semaphore(1, 1);
        private readonly int nextEndpointId;
        private readonly object incrementLock = new object();

        public IControllerHostLocal App { get; internal set; }

        public CLRCapabilities Capabilities { get; set; }

        public Controller(IControllerHostLocal app)
        {
            App = app;

            Random random = new Random();

            lastOutboundMessage = ushort.MaxValue;
            nextEndpointId = random.Next(int.MaxValue);

            //default capabilities
            Capabilities = new CLRCapabilities();
        }

        public Converter CreateConverter()
        {
            return new Converter(Capabilities);
        }

        private void ProcessExit()
        {
            App.ProcessExited();
        }

        public async Task<bool> SendAsync(MessageRaw raw, CancellationToken cancellationToken)
        {
            if(!_sendSemaphore.WaitOne())
            {
                return false;
            }

            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                return false;
            }

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
            catch (DeviceNotConnectedException)
            {
                App.ProcessExited();
            }
            catch
            {
                // catch everything else here, doesn't matter
                return false;
            }
            finally
            {
                _sendSemaphore.Release();
            }

            return false;
        }

        public ushort GetNextSequenceId()
        {
            lock (incrementLock)
            {
                lastOutboundMessage += 1;
            }
            return lastOutboundMessage;
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

        private async Task<uint> SendRawBufferAsync(byte[] buffer, TimeSpan waiTimeout, CancellationToken cancellationToken)
        {
            return await App.SendBufferAsync(buffer, waiTimeout, cancellationToken);
        }

        internal async Task<int> ReadBufferAsync(byte[] buffer, int offset, int bytesToRead, TimeSpan waitTimeout, CancellationToken cancellationToken)
        {
            if (!_receiveSemaphore.WaitOne())
            {
                return 0;
            }

            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                return 0;
            }

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
            catch (DeviceNotConnectedException)
            {
                App.ProcessExited();
            }
            catch (TaskCanceledException)
            {
                // don't do anything here, as this is expected
            }
            catch
            {
                // catch everything else, doesn't matter
                return 0;
            }
            finally
            {
                _receiveSemaphore.Release();
            }

            return bytesToReadRequested - bytesToRead;
        }
    }
}
