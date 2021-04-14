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

            Random random = new();

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

        public bool Send(MessageRaw raw)
        {
            if(!_sendSemaphore.WaitOne())
            {
                return false;
            }

            try
            {
                // TX header
                var sendHeaderCount = SendRawBuffer(raw.Header);

                if (raw.Payload != null)
                {
                    // we have a payload to TX
                    if (sendHeaderCount == raw.Header.Length)
                    {
                        var sendPayloadCount = SendRawBuffer(raw.Payload);

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
                throw;
            }
            catch (AggregateException)
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

        private int SendRawBuffer(byte[] buffer)
        {
            return App.SendBuffer(buffer);
        }

        internal int ReadBuffer(byte[] buffer, int offset, int bytesToRead)
        {
            if (!_receiveSemaphore.WaitOne())
            {
                return 0;
            }

            if (App.AvailableBytes < bytesToRead)
            {
                return 0;
            }

            try
            {
                // sanity check for anything to read
                if (bytesToRead == 0)
                {
                    return 0;
                }

                // read data
                var readResult = App.ReadBuffer(bytesToRead);

                // any byte read?
                if (readResult.Length > 0)
                {
                    Array.Copy(readResult, 0, buffer, offset, readResult.Length);

                    return readResult.Length;
                }
            }
            catch (DeviceNotConnectedException)
            {
                throw;
            }
            catch (TimeoutException)
            {
                // don't do anything here, as this is expected
            }
            catch (InvalidOperationException)
            {
                App.ProcessExited();
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

            return 0;
        }
    }
}
