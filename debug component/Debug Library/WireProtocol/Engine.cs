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
using Microsoft.SPOT.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Foundation.Metadata;
using Windows.Storage.Streams;

namespace Microsoft.SPOT.Debugger
{
    public class Engine : IDisposable, IControllerHostLocal
    {
        private const int RETRIES_DEFAULT = 4;
        private const int TIMEOUT_DEFAULT = 5000;

        //internal IControllerHostLocal<MFDevice> m_portDefinition;
        internal IPort m_portDefinition;
        internal Controller m_ctrl { get; set; }
        bool m_silent;
        bool m_stopDebuggerOnConnect;

        DateTime m_lastNoise = DateTime.Now;

        private static SemaphoreSlim semaphore;

        public event EventHandler<StringEventArgs> SpuriousCharactersReceived;

        //event NoiseEventHandler m_eventNoise;
        //event MessageEventHandler m_eventMessage;
        //event CommandEventHandler m_eventCommand;
        //event EventHandler m_eventProcessExit;

        /// <summary>
        /// Notification thread is essentially the Tx thread. Other threads pump outgoing data into it, which after potential
        /// processing is sent out to destination synchronously.
        /// </summary>
        //Thread m_notificationThread;
        AutoResetEvent m_notifyEvent;
        //ArrayList m_notifyQueue;
        FifoBuffer m_notifyNoise;

        AutoResetEvent m_rpcEvent;
        //ArrayList m_rpcQueue;
        //ArrayList m_rpcEndPoints;

        ManualResetEvent m_evtShutdown;
        ManualResetEvent m_evtPing;
        //ArrayList m_requests;
        TypeSysLookup m_typeSysLookup;
        //State m_state;
        //bool m_fProcessExited;

        bool m_fThrowOnCommunicationFailure;
        RebootTime m_RebootTime;
        internal IMFDevice Device;

        public Engine(IPort pd, IMFDevice device)
        {
            InitializeLocal(pd, device);
        }

        private void Initialize()
        {
            m_notifyEvent = new AutoResetEvent(false);
            m_rpcEvent = new AutoResetEvent(false);
            m_evtShutdown = new ManualResetEvent(false);
            m_evtPing = new ManualResetEvent(false);

            //m_rpcQueue = ArrayList.Synchronized(new ArrayList());
            //m_rpcEndPoints = ArrayList.Synchronized(new ArrayList());
            //m_requests = ArrayList.Synchronized(new ArrayList());
            //m_notifyQueue = ArrayList.Synchronized(new ArrayList());

            m_notifyNoise = new FifoBuffer();
            m_typeSysLookup = new TypeSysLookup();
            //m_state = new State(this);
            //m_fProcessExited = false;

            // Create the semaphore.
            semaphore = new SemaphoreSlim(1, 1);

            //default capabilities, used until clr can be queried.
            Capabilities = new CLRCapabilities();

            m_RebootTime = new RebootTime();

            // start task to tx spurious characters
            Task.Factory.StartNew(() => 
            {
                int read = 0;

                while (true)
                {
                    m_notifyNoise.WaitHandle.WaitOne();

                    while ((read = m_notifyNoise.Available) > 0)
                    {
                        byte[] buffer = new byte[m_notifyNoise.Available];

                        m_notifyNoise.Read(buffer, 0, buffer.Length);

                        if(SpuriousCharactersReceived != null)
                        {
                            SpuriousCharactersReceived.Invoke(this, new StringEventArgs(UTF8Encoding.UTF8.GetString(buffer, 0, buffer.Length)));
                        }
                    }

                }
            });

        }

        private void InitializeLocal(IPort pd, IMFDevice device)
        {
            m_portDefinition = pd;
            m_ctrl = new Controller(Packet.MARKER_PACKET_V1, this) ;

            this.Device = device;

            Initialize();
        }

        public CLRCapabilities Capabilities { get; internal set; }
        
        public bool IsConnected { get; internal set; }

        public ConnectionSource ConnectionSource { get; internal set; }

        public bool IsConnectedToTinyCLR
        {
            get { return ConnectionSource == ConnectionSource.TinyCLR; }
        }

        public bool IsTargetBigEndian { get; internal set; }

        public async Task<bool> ConnectAsync(int retries, int timeout, bool force = false, ConnectionSource connectionSource = ConnectionSource.Unknown)
        {
            if (force || IsConnected == false)
            {
                // connect to device 
                if (await Device.ConnectAsync().ConfigureAwait(false))
                {

                    Commands.Monitor_Ping cmd = new Commands.Monitor_Ping();

                    cmd.m_source = Commands.Monitor_Ping.c_Ping_Source_Host;
                    //cmd.m_dbg_flags = (m_stopDebuggerOnConnect ? Commands.Monitor_Ping.c_Ping_DbgFlag_Stop : 0);

                    IncomingMessage msg = await PerformRequestAsync(Commands.c_Monitor_Ping, Flags.c_NoCaching, cmd, retries, timeout).ConfigureAwait(false);

                    if (msg == null)
                    {
                        // disconnect device
                        Device.Disconnect();

                        // update flag
                        IsConnected = false;

                        // done here
                        return false;
                    }

                    Commands.Monitor_Ping.Reply reply = msg.Payload as Commands.Monitor_Ping.Reply;

                    if (reply != null)
                    {
                        IsTargetBigEndian = (reply.m_dbg_flags & Commands.Monitor_Ping.c_Ping_DbgFlag_BigEndian).Equals(Commands.Monitor_Ping.c_Ping_DbgFlag_BigEndian);
                    }

                    // update flag
                    IsConnected = true;

                    ConnectionSource = (reply == null || reply.m_source == Commands.Monitor_Ping.c_Ping_Source_TinyCLR) ? ConnectionSource.TinyCLR : ConnectionSource.TinyBooter;

                    if (m_silent)
                    {
                        await SetExecutionModeAsync(Commands.Debugging_Execution_ChangeConditions.c_fDebugger_Quiet, 0).ConfigureAwait(false);
                    }

                    // resume execution for older clients, since server tools no longer do this.
                    if (!m_stopDebuggerOnConnect && (msg != null && msg.Payload == null))
                    {
                        await ResumeExecutionAsync().ConfigureAwait(false);
                    }
                }
            }

            //if ((force || Capabilities.IsUnknown) && ConnectionSource == ConnectionSource.TinyCLR)
            //{
            //    Capabilities = await DiscoverCLRCapabilitiesAsync().ConfigureAwait(false);
            //    m_ctrl.Capabilities = Capabilities;
            //}

            if (connectionSource != ConnectionSource.Unknown && connectionSource != ConnectionSource)
            {
                // disconnect device
                Device.Disconnect();

                // update flag
                IsConnected = false;

                // done here
                return false;
            }

            return true;
        }

        public void Disconnect()
        {
            // better do this inside of try/catch because we can't be sure that the device is actually connected or that the 
            // operation can be successful
            try
            {
                // update flag
                IsConnected = false;

                // call disconnect at device level
                Device.Disconnect();
            }
            catch { }
        }

        public DateTime LastActivity
        {
            get
            {
                //return m_portDefinition.LastActivity;
                throw new NotImplementedException();

            }

            set { }
        }

        #region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).
                    try
                    {
                        if (IsConnected)
                        {
                            Device.Disconnect();
                        }
                    }
                    catch { }
                }

                // TODO: free unmanaged resources (unmanaged objects) and override a finalizer below.
                // TODO: set large fields to null.

                disposedValue = true;
            }
        }

        ~Engine()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(false);
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
            // TODO: uncomment the following line if the finalizer is overridden above.
            // GC.SuppressFinalize(this);
        }

        #endregion

        private async Task<IncomingMessage> PerformRequestAsync(uint command, uint flags, object payload, int retries = 0, int timeout = 2000)
        {
            await semaphore.WaitAsync().ConfigureAwait(false);

            IncomingMessage replyMessage;

            try
            {
                //Debug.WriteLine("_________________________________________________________");
                //Debug.WriteLine("Executing " + DebuggerEventSource.GetCommandName(command));
                //Debug.WriteLine("_________________________________________________________");

                // create message
                OutgoingMessage message = new OutgoingMessage(m_ctrl, CreateConverter(), command, flags, payload);

                // create request 
                Request request = new Request(m_ctrl, message, retries, timeout, null);
                replyMessage = await request.PerformRequestAsync(new CancellationToken()).ConfigureAwait(false);
            }
            finally
            {
                semaphore.Release();
            }

            return replyMessage;
        }

        private async Task<IncomingMessage> PerformRequestAsync(OutgoingMessage message, int retries = 3, int timeout = 500)
        {
            // create request 
            Request request = new Request(m_ctrl, message, retries, timeout, null);

            return await request.PerformRequestAsync(new CancellationToken());
        }

        private async Task<List<IncomingMessage>> PerformRequestBatchAsync(List<OutgoingMessage> messages, int retries = 3, int timeout = 1000)
        {
            List<IncomingMessage> replies = new List<IncomingMessage>();
            List<Request> requests = new List<Request>();

            foreach(OutgoingMessage message in messages)
            {
                replies.Add(await PerformRequestAsync(message, retries, timeout));
            }

            return replies;
        }

        public async Task<Commands.Monitor_Ping.Reply> GetConnectionSourceAsync()
        {
            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_Ping, 0, null, 2, 500).ConfigureAwait(false);

            if (reply != null)
            {
                return reply.Payload as Commands.Monitor_Ping.Reply;
            }

            return null;
        }

        internal Converter CreateConverter()
        {
            return new Converter(Capabilities);
        }
        
        public async Task<uint> SendBufferAsync(byte[] buffer, TimeSpan waiTimeout, CancellationToken cancellationToken)
        {
            return await m_portDefinition.SendBufferAsync(buffer, waiTimeout, cancellationToken).ConfigureAwait(false);
        }

        public bool ProcessMessage(IncomingMessage msg, bool fReply)
        {
            throw new NotImplementedException();
        }

        public void SpuriousCharacters(byte[] buf, int offset, int count)
        {
            m_lastNoise = DateTime.Now;

            m_notifyNoise.Write(buf, offset, count);
        }

        public void ProcessExited()
        {
            throw new NotImplementedException();
        }

        public async Task<DataReader> ReadBufferAsync(uint bytesToRead, TimeSpan waitTimeout, CancellationToken cancellationToken)
        {
            return await m_portDefinition.ReadBufferAsync(bytesToRead, waitTimeout, cancellationToken).ConfigureAwait(false);
        }

        private OutgoingMessage CreateMessage(uint cmd, uint flags, object payload)
        {
            return new OutgoingMessage(m_ctrl, CreateConverter(), cmd, flags, payload);
        }


        #region Commands implementation

        public async Task<List<Commands.Monitor_MemoryMap.Range>> GetMemoryMapAsync()
        {
            Commands.Monitor_MemoryMap cmd = new Commands.Monitor_MemoryMap();

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_MemoryMap, 0, cmd).ConfigureAwait(false);

            if (reply != null)
            {
                Commands.Monitor_MemoryMap.Reply cmdReply = reply.Payload as Commands.Monitor_MemoryMap.Reply;

                if (cmdReply != null)
                {
                    return cmdReply.m_map;
                }
            }

            return null;
        }

        public async Task<List<Commands.Monitor_DeploymentMap.DeploymentData>> GetDeploymentMapAsync()
        {
            Commands.Monitor_DeploymentMap cmd = new Commands.Monitor_DeploymentMap();

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_DeploymentMap, 0, cmd, 2, 10000).ConfigureAwait(false);

            if (reply != null)
            {
                Commands.Monitor_DeploymentMap.Reply cmdReply = reply.Payload as Commands.Monitor_DeploymentMap.Reply;

                if (cmdReply != null)
                {
                    return cmdReply.m_map;
                }
            }

            return null;
        }

        public async Task<Commands.Monitor_OemInfo.Reply> GetMonitorOemInfo()
        {
            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_OemInfo, 0, null, 2, 1000).ConfigureAwait(false);

            if (reply != null)
            {
                return reply.Payload as Commands.Monitor_OemInfo.Reply;
            }

            return null;
        }

        public async Task<List<Commands.Monitor_FlashSectorMap.FlashSectorData>> GetFlashSectorMapAsync()
        {
            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_FlashSectorMap, 0, null, 1, 4000).ConfigureAwait(false);

            if (reply != null)
            {
                var cmdReply = reply.Payload as Commands.Monitor_FlashSectorMap.Reply;

                if (cmdReply != null)
                {
                    return cmdReply.m_map;
                }
            }

            return null;
        }

        public async Task<bool> UpdateSignatureKeyAsync(PublicKeyIndex keyIndex, byte[] oldPublicKeySignature, byte[] newPublicKey, byte[] reserveData)
        {
            Commands.Monitor_SignatureKeyUpdate keyUpdate = new Commands.Monitor_SignatureKeyUpdate();

            // key must be 260 bytes
            if (keyUpdate.m_newPublicKey.Length != newPublicKey.Length)
                return false;

            if (!keyUpdate.PrepareForSend((uint)keyIndex, oldPublicKeySignature, newPublicKey, reserveData))
                return false;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_SignatureKeyUpdate, 0, keyUpdate).ConfigureAwait(false);

            return IncomingMessage.IsPositiveAcknowledge(reply);
        }

        private async Task<Tuple<byte[], bool>> ReadMemoryAsync(uint address, uint length, uint offset)
        {
            byte[] buffer = new byte[length];

            while (length > 0)
            {
                Commands.Monitor_ReadMemory cmd = new Commands.Monitor_ReadMemory();

                cmd.m_address = address;
                cmd.m_length = length;

                IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_ReadMemory, 0, cmd).ConfigureAwait(false);
                if (reply == null)
                {
                    return new Tuple<byte[], bool>(new byte[0], false);
                }

                Commands.Monitor_ReadMemory.Reply cmdReply = reply.Payload as Commands.Monitor_ReadMemory.Reply;

                if (cmdReply == null || cmdReply.m_data == null)
                {
                    return new Tuple<byte[], bool>(new byte[0], false);
                }

                uint actualLength = Math.Min((uint)cmdReply.m_data.Length, length);

                Array.Copy(cmdReply.m_data, 0, buffer, (int)offset, (int)actualLength);

                address += actualLength;
                length -= actualLength;
                offset += actualLength;
            }

            return new Tuple<byte[], bool>(new byte[0], true);
        }

        public async Task<Tuple<byte[], bool>> ReadMemoryAsync(uint address, uint length)
        {
            return await ReadMemoryAsync(address, length, 0).ConfigureAwait(false);
        }

        public async Task<bool> WriteMemoryAsync(uint address, byte[] buf, int offset, int length)
        {
            int count = length;
            int pos = offset;

            while (count > 0)
            {
                Commands.Monitor_WriteMemory cmd = new Commands.Monitor_WriteMemory();
                int len = Math.Min(1024, count);

                cmd.PrepareForSend(address, buf, pos, len);

                DebuggerEventSource.Log.EngineWriteMemory(address, len);

                IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_WriteMemory, 0, cmd, 0, 20000).ConfigureAwait(false);

                if (!IncomingMessage.IsPositiveAcknowledge(reply))
                {
                    return false;
                }

                address += (uint)len;
                count -= len;
                pos += len;
            }

            return true;
        }

        public async Task<bool> WriteMemoryAsync(uint address, byte[] buf)
        {
            return await WriteMemoryAsync(address, buf, 0, buf.Length).ConfigureAwait(false);
        }

        public async Task<bool> CheckSignatureAsync(byte[] signature, uint keyIndex)
        {
            Commands.Monitor_Signature cmd = new Commands.Monitor_Signature();

            cmd.PrepareForSend(signature, keyIndex);

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_CheckSignature, 0, cmd, 0, 600000).ConfigureAwait(false);

            return IncomingMessage.IsPositiveAcknowledge(reply);
        }

        public async Task<bool> EraseMemoryAsync(uint address, uint length)
        {
            DebuggerEventSource.Log.EngineEraseMemory(address, length);

            var cmd = new Commands.Monitor_EraseMemory
            {
                m_address = address,
                m_length = length
            };

            // Magic number multiplier here is somewhat arbitrary. Assuming a max 250ms per 1KB block erase time.
            // (Given most chip erase times are measured in uSecs that's pretty generous 8^) )
            // The idea is to extend the timeout based on the actual length of the area being erased
            var timeout = (int)(length / 1024) * 250;
            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_EraseMemory, 0, cmd, 2, timeout).ConfigureAwait(false);

            return IncomingMessage.IsPositiveAcknowledge(reply);
        }

        public async Task<bool> ExecuteMemoryAsync(uint address)
        {
            Commands.Monitor_Execute cmd = new Commands.Monitor_Execute();

            cmd.m_address = address;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_Execute, 0, cmd).ConfigureAwait(false);

            return IncomingMessage.IsPositiveAcknowledge(reply);
        }

        public async Task RebootDeviceAsync(RebootOption option = RebootOption.NormalReboot)
        {
            Commands.Monitor_Reboot cmd = new Commands.Monitor_Reboot();

            bool fThrowOnCommunicationFailureSav = m_fThrowOnCommunicationFailure;

            m_fThrowOnCommunicationFailure = false;

            switch (option)
            {
                case RebootOption.EnterBootloader:
                    cmd.m_flags = Commands.Monitor_Reboot.c_EnterBootloader;
                    break;
                case RebootOption.RebootClrOnly:
                    cmd.m_flags = Capabilities.SoftReboot ? Commands.Monitor_Reboot.c_ClrRebootOnly : Commands.Monitor_Reboot.c_NormalReboot;
                    break;
                case RebootOption.RebootClrWaitForDebugger:
                    cmd.m_flags = Capabilities.SoftReboot ? Commands.Monitor_Reboot.c_ClrWaitForDbg : Commands.Monitor_Reboot.c_NormalReboot;
                    break;
                default:
                    cmd.m_flags = Commands.Monitor_Reboot.c_NormalReboot;
                    break;
            }

            try
            {
                m_evtPing.Reset();

                await PerformRequestAsync(Commands.c_Monitor_Reboot, 0, cmd).ConfigureAwait(false);

                if (option != RebootOption.NoReconnect)
                {
                    //int timeout = 1000;

                    //if (m_portDefinition is PortDefinition_Tcp)
                    //{
                    //    timeout = 2000;
                    //}

                    //Thread.Sleep(timeout);
                }
            }
            finally
            {
                m_fThrowOnCommunicationFailure = fThrowOnCommunicationFailureSav;
            }

        }

        public async Task<bool> ReconnectAsync(bool fSoftReboot)
        {
            if (!await ConnectAsync(m_RebootTime.Retries, m_RebootTime.WaitMs(fSoftReboot), true, ConnectionSource.Unknown).ConfigureAwait(false))
            {
                if (m_fThrowOnCommunicationFailure)
                {
                    throw new Exception("Could not reconnect to TinyCLR");
                }
                return false;
            }

            return true;
        }

        public async Task<uint> GetExecutionBasePtrAsync()
        {
            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Execution_BasePtr, 0, null).ConfigureAwait(false);
            if (reply != null)
            {
                Commands.Debugging_Execution_BasePtr.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_BasePtr.Reply;

                if (cmdReply != null)
                {
                    return cmdReply.m_EE;
                }
            }

            return 0;
        }

        public async Task<Tuple<uint, bool>> SetExecutionModeAsync(uint iSet, uint iReset)
        {
            Commands.Debugging_Execution_ChangeConditions cmd = new Commands.Debugging_Execution_ChangeConditions();

            cmd.m_set = iSet;
            cmd.m_reset = iReset;

            uint iCurrent;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Execution_ChangeConditions, Flags.c_NoCaching, cmd).ConfigureAwait(false);
            if (reply != null)
            {
                Commands.Debugging_Execution_ChangeConditions.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_ChangeConditions.Reply;

                if (cmdReply != null)
                {
                    return new Tuple<uint, bool>(cmdReply.m_current, true);
                }
                else
                {
                    return new Tuple<uint, bool>(0, true);
                }
            }

            return new Tuple<uint, bool>(0, false);
        }

        //public async Task<bool> SetExecutionModeAsync(uint iSet, uint iReset)
        //{
        //    uint iCurrent;

        //    return SetExecutionMode(iSet, iReset, out iCurrent);
        //}

        public async Task<bool> PauseExecutionAsync()
        {
            var ret = await SetExecutionModeAsync(Commands.Debugging_Execution_ChangeConditions.c_Stopped, 0).ConfigureAwait(false);

            return ret.Item2;
        }

        public async Task<bool> ResumeExecutionAsync()
        {
            var ret = await SetExecutionModeAsync(0, Commands.Debugging_Execution_ChangeConditions.c_Stopped).ConfigureAwait(false);

            return ret.Item2;
        }

        public async Task<bool> SetCurrentAppDomainAsync(uint id)
        {
            Commands.Debugging_Execution_SetCurrentAppDomain cmd = new Commands.Debugging_Execution_SetCurrentAppDomain();

            cmd.m_id = id;

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Execution_SetCurrentAppDomain, 0, cmd).ConfigureAwait(false));
        }

        public async Task<bool> SetBreakpointsAsync(Commands.Debugging_Execution_BreakpointDef[] breakpoints)
        {
            Commands.Debugging_Execution_Breakpoints cmd = new Commands.Debugging_Execution_Breakpoints();

            cmd.m_data = breakpoints;

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Execution_Breakpoints, 0, cmd).ConfigureAwait(false));
        }

        public async Task<Commands.Debugging_Execution_BreakpointDef> GetBreakpointStatusAsync()
        {
            Commands.Debugging_Execution_BreakpointStatus cmd = new Commands.Debugging_Execution_BreakpointStatus();

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Execution_BreakpointStatus, 0, cmd).ConfigureAwait(false);

            if (reply != null)
            {
                Commands.Debugging_Execution_BreakpointStatus.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_BreakpointStatus.Reply;

                if (cmdReply != null)
                    return cmdReply.m_lastHit;
            }

            return null;
        }

        public async Task<bool> SetSecurityKeyAsync(byte[] key)
        {
            Commands.Debugging_Execution_SecurityKey cmd = new Commands.Debugging_Execution_SecurityKey();

            cmd.m_key = key;

            return await PerformRequestAsync(Commands.c_Debugging_Execution_SecurityKey, 0, cmd).ConfigureAwait(false) != null;
        }

        public async Task<bool> UnlockDeviceAsync(byte[] blob)
        {
            Commands.Debugging_Execution_Unlock cmd = new Commands.Debugging_Execution_Unlock();

            Array.Copy(blob, 0, cmd.m_command, 0, 128);
            Array.Copy(blob, 128, cmd.m_hash, 0, 128);

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Execution_Unlock, 0, cmd).ConfigureAwait(false));
        }

        public async Task<Tuple<uint, bool>> AllocateMemoryAsync(uint size)
        {
            Commands.Debugging_Execution_Allocate cmd = new Commands.Debugging_Execution_Allocate();

            cmd.m_size = size;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Execution_Allocate, 0, cmd).ConfigureAwait(false);
            if (reply != null)
            {
                Commands.Debugging_Execution_Allocate.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_Allocate.Reply;

                if (cmdReply != null)
                {
                    return new Tuple<uint, bool>(cmdReply.m_address, true);
                }
            }

            return new Tuple<uint, bool>(0, false);
        }

        //public IAsyncResult UpgradeConnectionToSsl_Begin(X509Certificate2 cert, bool fRequireClientCert)
        //{
        //    AsyncNetworkStream ans = ((IControllerLocal)m_ctrl).OpenPort() as AsyncNetworkStream;

        //    if (ans == null)
        //        return null;

        //    m_ctrl.StopProcessing();

        //    IAsyncResult iar = ans.BeginUpgradeToSSL(cert, fRequireClientCert);

        //    return iar;
        //}

        //public async Task<bool> UpgradeConnectionToSSL_End(IAsyncResult iar)
        //{
        //    AsyncNetworkStream ans = ((IControllerLocal)m_ctrl).OpenPort() as AsyncNetworkStream;

        //    if (ans == null)
        //        return false;

        //    bool result = ans.EndUpgradeToSSL(iar);

        //    m_ctrl.ResumeProcessing();

        //    return result;
        //}

        //public bool IsUsingSsl
        //{
        //    get
        //    {
        //        if (!IsConnected)
        //            return false;

        //        AsyncNetworkStream ans = ((IControllerLocal)m_ctrl).OpenPort() as AsyncNetworkStream;

        //        if (ans == null)
        //            return false;

        //        return ans.IsUsingSsl;
        //    }
        //}

        public async Task<bool> CanUpgradeToSslAsync()
        {
            Commands.Debugging_UpgradeToSsl cmd = new Commands.Debugging_UpgradeToSsl();

            cmd.m_flags = 0;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_UpgradeToSsl, Flags.c_NoCaching, cmd, 2, 5000).ConfigureAwait(false);

            if (reply != null)
            {
                Commands.Debugging_UpgradeToSsl.Reply cmdReply = reply.Payload as Commands.Debugging_UpgradeToSsl.Reply;

                if (cmdReply != null)
                {
                    return cmdReply.m_success != 0;
                }
            }

            return false;

        }

        Dictionary<int, uint[]> m_updateMissingPktTbl = new Dictionary<int, uint[]>();

       

        /// <summary>
        /// 
        /// </summary>
        /// <param name="provider"></param>
        /// <param name="versionMajor"></param>
        /// <param name="versionMinor"></param>
        /// <param name="updateId"></param>
        /// <param name="updateType"></param>
        /// <param name="updateSubType"></param>
        /// <param name="updateSize"></param>
        /// <param name="packetSize"></param>
        /// <param name="installAddress"></param>
        /// <returns>The update handle value. -1 if the start update request failed.</returns>
        public async Task<int> StartUpdateAsync(
            string provider,
            ushort versionMajor,
            ushort versionMinor,
            uint updateId,
            uint updateType,
            uint updateSubType,
            uint updateSize,
            uint packetSize,
            uint installAddress)
        {
            Commands.Debugging_MFUpdate_Start cmd = new Commands.Debugging_MFUpdate_Start();

            byte[] name = UTF8Encoding.UTF8.GetBytes(provider);

            Array.Copy(name, cmd.m_updateProvider, Math.Min(name.Length, cmd.m_updateProvider.Length));
            cmd.m_updateId = updateId;
            cmd.m_updateVerMajor = versionMajor;
            cmd.m_updateVerMinor = versionMinor;
            cmd.m_updateType = updateType;
            cmd.m_updateSubType = updateSubType;
            cmd.m_updateSize = updateSize;
            cmd.m_updatePacketSize = packetSize;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_MFUpdate_Start, Flags.c_NoCaching, cmd, 2, 5000).ConfigureAwait(false);

            if (reply != null)
            {
                Commands.Debugging_MFUpdate_Start.Reply cmdReply = reply.Payload as Commands.Debugging_MFUpdate_Start.Reply;

                if (cmdReply != null)
                {
                    return cmdReply.m_updateHandle;
                }
            }

            return -1;
        }

        public async Task<Tuple<byte[], bool>> UpdateAuthCommandAsync(int updateHandle, uint authCommand, byte[] commandArgs)
        {
            Commands.Debugging_MFUpdate_AuthCommand cmd = new Commands.Debugging_MFUpdate_AuthCommand();

            if (commandArgs == null)
                commandArgs = new byte[0];

            cmd.m_updateHandle = updateHandle;
            cmd.m_authCommand = authCommand;
            cmd.m_authArgs = commandArgs;
            cmd.m_authArgsSize = (uint)commandArgs.Length;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_MFUpdate_AuthCmd, Flags.c_NoCaching, cmd).ConfigureAwait(false);

            if (reply != null)
            {
                Commands.Debugging_MFUpdate_AuthCommand.Reply cmdReply = reply.Payload as Commands.Debugging_MFUpdate_AuthCommand.Reply;

                if (cmdReply != null && cmdReply.m_success != 0)
                {
                    if (cmdReply.m_responseSize > 0)
                    {
                        byte[] response = new byte[4];
                        Array.Copy(cmdReply.m_response, response, Math.Min(response.Length, (int)cmdReply.m_responseSize));

                        return new Tuple<byte[], bool>(response, true);
                    }
                }
            }

            return new Tuple<byte[], bool>(new byte[4], true);
        }

        public async Task<bool> UpdateAuthenticateAsync(int updateHandle, byte[] authenticationData)
        {
            Commands.Debugging_MFUpdate_Authenticate cmd = new Commands.Debugging_MFUpdate_Authenticate();

            cmd.m_updateHandle = updateHandle;
            cmd.PrepareForSend(authenticationData);

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_MFUpdate_Authenticate, Flags.c_NoCaching, cmd).ConfigureAwait(false);

            if (reply != null)
            {
                Commands.Debugging_MFUpdate_Authenticate.Reply cmdReply = reply.Payload as Commands.Debugging_MFUpdate_Authenticate.Reply;

                if (cmdReply != null && cmdReply.m_success != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private async Task<bool> UpdateGetMissingPacketsAsync(int updateHandle)
        {
            Commands.Debugging_MFUpdate_GetMissingPkts cmd = new Commands.Debugging_MFUpdate_GetMissingPkts();

            cmd.m_updateHandle = updateHandle;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_MFUpdate_GetMissingPkts, Flags.c_NoCaching, cmd).ConfigureAwait(false);

            if (reply != null)
            {
                Commands.Debugging_MFUpdate_GetMissingPkts.Reply cmdReply = reply.Payload as Commands.Debugging_MFUpdate_GetMissingPkts.Reply;

                if (cmdReply != null && cmdReply.m_success != 0)
                {
                    if (cmdReply.m_missingPktCount > 0)
                    {
                        m_updateMissingPktTbl[updateHandle] = cmdReply.m_missingPkts;
                    }
                    else
                    {
                        m_updateMissingPktTbl[updateHandle] = new uint[0];
                    }
                    return true;
                }
            }

            return false;
        }

        public async Task<bool> AddPacketAsync(int updateHandle, uint packetIndex, byte[] packetData, uint packetValidation)
        {
            if (!m_updateMissingPktTbl.ContainsKey(updateHandle))
            {
                await UpdateGetMissingPacketsAsync(updateHandle).ConfigureAwait(false);
            }

            if (m_updateMissingPktTbl.ContainsKey(updateHandle) && m_updateMissingPktTbl[updateHandle].Length > 0)
            {
                uint[] pktBits = m_updateMissingPktTbl[updateHandle];
                uint div = packetIndex >> 5;

                if (pktBits.Length > div)
                {
                    if (0 == (pktBits[div] & (1u << (int)(packetIndex % 32))))
                    {
                        return true;
                    }
                }
            }

            Commands.Debugging_MFUpdate_AddPacket cmd = new Commands.Debugging_MFUpdate_AddPacket();

            cmd.m_updateHandle = updateHandle;
            cmd.m_packetIndex = packetIndex;
            cmd.m_packetValidation = packetValidation;
            cmd.PrepareForSend(packetData);

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_MFUpdate_AddPacket, Flags.c_NoCaching, cmd).ConfigureAwait(false);
            if (reply != null)
            {
                Commands.Debugging_MFUpdate_AddPacket.Reply cmdReply = reply.Payload as Commands.Debugging_MFUpdate_AddPacket.Reply;

                if (cmdReply != null)
                {
                    return cmdReply.m_success != 0;
                }
            }

            return false;
        }

        public async Task<bool> InstallUpdateAsync(int updateHandle, byte[] validationData)
        {
            if (m_updateMissingPktTbl.ContainsKey(updateHandle))
            {
                m_updateMissingPktTbl.Remove(updateHandle);
            }

            Commands.Debugging_MFUpdate_Install cmd = new Commands.Debugging_MFUpdate_Install();

            cmd.m_updateHandle = updateHandle;

            cmd.PrepareForSend(validationData);

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_MFUpdate_Install, Flags.c_NoCaching, cmd).ConfigureAwait(false);

            if (reply != null)
            {
                Commands.Debugging_MFUpdate_Install.Reply cmdReply = reply.Payload as Commands.Debugging_MFUpdate_Install.Reply;

                if (cmdReply != null)
                {
                    return cmdReply.m_success != 0;
                }
            }

            return false;
        }

        public async Task<uint> CreateThreadAsync(uint methodIndex, int scratchPadLocation)
        {
            return await CreateThreadAsync(methodIndex, scratchPadLocation, 0).ConfigureAwait(false);
        }

        public async Task<uint> CreateThreadAsync(uint methodIndex, int scratchPadLocation, uint pid)
        {
            if (Capabilities.ThreadCreateEx)
            {
                Commands.Debugging_Thread_CreateEx cmd = new Commands.Debugging_Thread_CreateEx();

                cmd.m_md = methodIndex;
                cmd.m_scratchPad = scratchPadLocation;
                cmd.m_pid = pid;

                IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Thread_CreateEx, 0, cmd).ConfigureAwait(false);

                if (reply != null)
                {
                    Commands.Debugging_Thread_CreateEx.Reply cmdReply = reply.Payload as Commands.Debugging_Thread_CreateEx.Reply;

                    return cmdReply.m_pid;
                }
            }

            return 0;
        }

        public async Task<uint[]> GetThreadListAsync()
        {
            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Thread_List, 0, null).ConfigureAwait(false);

            if (reply != null)
            {
                Commands.Debugging_Thread_List.Reply cmdReply = reply.Payload as Commands.Debugging_Thread_List.Reply;

                if (cmdReply != null)
                {
                    return cmdReply.m_pids;
                }
            }

            return null;
        }

        public async Task<Commands.Debugging_Thread_Stack.Reply> GetThreadStackAsync(uint pid)
        {
            Commands.Debugging_Thread_Stack cmd = new Commands.Debugging_Thread_Stack();

            cmd.m_pid = pid;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Thread_Stack, 0, cmd).ConfigureAwait(false);

            if (reply != null)
            {
                return reply.Payload as Commands.Debugging_Thread_Stack.Reply;
            }

            return null;
        }

        public async Task<bool> KillThreadAsync(uint pid)
        {
            Commands.Debugging_Thread_Kill cmd = new Commands.Debugging_Thread_Kill();

            cmd.m_pid = pid;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Thread_Kill, 0, cmd).ConfigureAwait(false);

            if (reply != null)
            {
                Commands.Debugging_Thread_Kill.Reply cmdReply = reply.Payload as Commands.Debugging_Thread_Kill.Reply;

                return cmdReply.m_result != 0;
            }

            return false;
        }

        public async Task<bool> SuspendThreadAsync(uint pid)
        {
            Commands.Debugging_Thread_Suspend cmd = new Commands.Debugging_Thread_Suspend();

            cmd.m_pid = pid;

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Thread_Suspend, 0, cmd).ConfigureAwait(false));
        }

        public async Task<bool> ResumeThreadAsync(uint pid)
        {
            Commands.Debugging_Thread_Resume cmd = new Commands.Debugging_Thread_Resume();

            cmd.m_pid = pid;

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Thread_Resume, 0, cmd).ConfigureAwait(false));
        }

        public async Task<RuntimeValue> GetThreadException(uint pid)
        {
            Commands.Debugging_Thread_GetException cmd = new Commands.Debugging_Thread_GetException();

            cmd.m_pid = pid;

            return await GetRuntimeValueAsync(Commands.c_Debugging_Thread_GetException, cmd).ConfigureAwait(false);
        }

        public async Task<RuntimeValue> GetThreadAsync(uint pid)
        {
            Commands.Debugging_Thread_Get cmd = new Commands.Debugging_Thread_Get();

            cmd.m_pid = pid;

            return await GetRuntimeValueAsync(Commands.c_Debugging_Thread_Get, cmd).ConfigureAwait(false);
        }

        public async Task<bool> UnwindThreadAsync(uint pid, uint depth)
        {
            Commands.Debugging_Thread_Unwind cmd = new Commands.Debugging_Thread_Unwind();

            cmd.m_pid = pid;
            cmd.m_depth = depth;

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Thread_Unwind, 0, cmd).ConfigureAwait(false));
        }

        public async Task<bool> SetIPOfStackFrameAsync(uint pid, uint depth, uint IP, uint depthOfEvalStack)
        {
            Commands.Debugging_Stack_SetIP cmd = new Commands.Debugging_Stack_SetIP();

            cmd.m_pid = pid;
            cmd.m_depth = depth;

            cmd.m_IP = IP;
            cmd.m_depthOfEvalStack = depthOfEvalStack;

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Stack_SetIP, 0, cmd).ConfigureAwait(false));
        }

        public async Task<Commands.Debugging_Stack_Info.Reply> GetStackInfoAsync(uint pid, uint depth)
        {
            Commands.Debugging_Stack_Info cmd = new Commands.Debugging_Stack_Info();

            cmd.m_pid = pid;
            cmd.m_depth = depth;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Stack_Info, 0, cmd).ConfigureAwait(false);

            if (reply != null)
            {
                return reply.Payload as Commands.Debugging_Stack_Info.Reply;
            }

            return null;
        }

        //--//

        public async Task<Commands.Debugging_TypeSys_AppDomains.Reply> GetAppDomainsAsync()
        {
            if (!Capabilities.AppDomains)
                return null;

            Commands.Debugging_TypeSys_AppDomains cmd = new Commands.Debugging_TypeSys_AppDomains();

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_TypeSys_AppDomains, 0, cmd).ConfigureAwait(false);

            if (reply != null)
            {
                return reply.Payload as Commands.Debugging_TypeSys_AppDomains.Reply;
            }

            return null;
        }

        public async Task<Commands.Debugging_TypeSys_Assemblies.Reply> GetAssembliesAsync()
        {
            Commands.Debugging_TypeSys_Assemblies cmd = new Commands.Debugging_TypeSys_Assemblies();

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_TypeSys_Assemblies, 0, cmd).ConfigureAwait(false);

            if (reply != null)
            {
                return reply.Payload as Commands.Debugging_TypeSys_Assemblies.Reply;
            }

            return null;
        }

        public async Task<List<Commands.Debugging_Resolve_Assembly>> ResolveAllAssembliesAsync()
        {
            Commands.Debugging_TypeSys_Assemblies.Reply assemblies = await GetAssembliesAsync().ConfigureAwait(false);
            List<Commands.Debugging_Resolve_Assembly> resolveAssemblies = new List<Commands.Debugging_Resolve_Assembly>();

            if (assemblies == null || assemblies.m_data == null)
            {
                resolveAssemblies = new List<Commands.Debugging_Resolve_Assembly>();
            }
            else
            {
                List<OutgoingMessage> requests = new List<OutgoingMessage>();

                foreach(uint iAssembly in assemblies.m_data)
                {
                    Commands.Debugging_Resolve_Assembly cmd = new Commands.Debugging_Resolve_Assembly();
                    cmd.m_idx = iAssembly;

                    requests.Add(CreateMessage(Commands.c_Debugging_Resolve_Assembly, 0, cmd));
                }

                List<IncomingMessage> replies = await PerformRequestBatchAsync(requests).ConfigureAwait(false);

                foreach(IncomingMessage message in replies)
                {
                    // reply is a match for request which m_seq is same as reply m_seqReply
                    resolveAssemblies.Add(requests.Find(req => req.Header.m_seq == message.Header.m_seqReply).Payload as Commands.Debugging_Resolve_Assembly);
                    resolveAssemblies[resolveAssemblies.Count - 1].m_reply = message.Payload as Commands.Debugging_Resolve_Assembly.Reply;
                }
            }

            return resolveAssemblies;
        }

        public async Task<Commands.Debugging_Resolve_Assembly.Reply> ResolveAssemblyAsync(uint idx)
        {
            Commands.Debugging_Resolve_Assembly cmd = new Commands.Debugging_Resolve_Assembly();

            cmd.m_idx = idx;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Resolve_Assembly, 0, cmd).ConfigureAwait(false);

            if (reply != null)
            {
                return reply.Payload as Commands.Debugging_Resolve_Assembly.Reply;
            }

            return null;
        }

        public enum StackValueKind
        {
            Local = 0,
            Argument = 1,
            EvalStack = 2,
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="pid"></param>
        /// <param name="depth"></param>
        /// <returns>Tuple with numOfArguments, numOfLocals, depthOfEvalStack and request success result.</returns>
        public async Task<Tuple<uint, uint, uint, bool>> GetStackFrameInfoAsync(uint pid, uint depth)
        {
            Commands.Debugging_Stack_Info.Reply reply = await GetStackInfoAsync(pid, depth).ConfigureAwait(false);

            if (reply == null)
            {
                return new Tuple<uint, uint, uint, bool>(0, 0, 0, false);
            }

            return new Tuple<uint, uint, uint, bool>(reply.m_numOfArguments, reply.m_numOfLocals, reply.m_depthOfEvalStack, true);
        }

        private async Task<RuntimeValue> GetRuntimeValueAsync(uint msg, object cmd)
        {
            IncomingMessage reply = await PerformRequestAsync(msg, 0, cmd).ConfigureAwait(false);

            if (reply != null && reply.Payload != null)
            {
                Commands.Debugging_Value_Reply cmdReply = reply.Payload as Commands.Debugging_Value_Reply;

                return RuntimeValue.Convert(this, cmdReply.m_values);
            }

            return null;
        }

        internal async Task<RuntimeValue> GetFieldValueAsync(RuntimeValue val, uint offset, uint fd)
        {
            Commands.Debugging_Value_GetField cmd = new Commands.Debugging_Value_GetField();

            cmd.m_heapblock = (val == null ? 0 : val.m_handle.m_referenceID);
            cmd.m_offset = offset;
            cmd.m_fd = fd;

            return await GetRuntimeValueAsync(Commands.c_Debugging_Value_GetField, cmd).ConfigureAwait(false);
        }

        public async Task<RuntimeValue> GetStaticFieldValueAsync(uint fd)
        {
            return await GetFieldValueAsync(null, 0, fd).ConfigureAwait(false);
        }

        internal async Task<RuntimeValue> AssignRuntimeValueAsync(uint heapblockSrc, uint heapblockDst)
        {
            Commands.Debugging_Value_Assign cmd = new Commands.Debugging_Value_Assign();

            cmd.m_heapblockSrc = heapblockSrc;
            cmd.m_heapblockDst = heapblockDst;

            return await GetRuntimeValueAsync(Commands.c_Debugging_Value_Assign, cmd).ConfigureAwait(false);
        }

        internal async Task<bool> SetBlockAsync(uint heapblock, uint dt, byte[] data)
        {
            Commands.Debugging_Value_SetBlock setBlock = new Commands.Debugging_Value_SetBlock();

            setBlock.m_heapblock = heapblock;
            setBlock.m_dt = dt;

            data.CopyTo(setBlock.m_value, 0);

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Value_SetBlock, 0, setBlock).ConfigureAwait(false));
        }

        private OutgoingMessage CreateMessage_GetValue_Stack(uint pid, uint depth, StackValueKind kind, uint index)
        {
            Commands.Debugging_Value_GetStack cmd = new Commands.Debugging_Value_GetStack();

            cmd.m_pid = pid;
            cmd.m_depth = depth;
            cmd.m_kind = (uint)kind;
            cmd.m_index = index;

            return CreateMessage(Commands.c_Debugging_Value_GetStack, 0, cmd);
        }

        public async Task<bool> ResizeScratchPadAsync(int size)
        {
            Commands.Debugging_Value_ResizeScratchPad cmd = new Commands.Debugging_Value_ResizeScratchPad();

            cmd.m_size = size;

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Value_ResizeScratchPad, 0, cmd).ConfigureAwait(false));
        }

        public async Task<RuntimeValue> GetStackFrameValueAsync(uint pid, uint depth, StackValueKind kind, uint index)
        {
            OutgoingMessage cmd = CreateMessage_GetValue_Stack(pid, depth, kind, index);

            IncomingMessage reply = await PerformRequestAsync(cmd, 10, 200).ConfigureAwait(false);

            if (reply != null)
            {
                Commands.Debugging_Value_Reply cmdReply = reply.Payload as Commands.Debugging_Value_Reply;

                return RuntimeValue.Convert(this, cmdReply.m_values);
            }

            return null;
        }

        public async Task<RuntimeValue[]> GetStackFrameValueAllAsync(uint pid, uint depth, uint cValues, StackValueKind kind)
        {
            List<OutgoingMessage> cmds = new List<OutgoingMessage>();
            RuntimeValue[] vals = null;
            uint i = 0;

            for (i = 0; i < cValues; i++)
            {
                cmds.Add(CreateMessage_GetValue_Stack(pid, depth, kind, i));
            }

            List<IncomingMessage> replies = await PerformRequestBatchAsync(cmds).ConfigureAwait(false);

            if (replies != null)
            {
                vals = new RuntimeValue[cValues];

                foreach(IncomingMessage message in replies)
                {
                    Commands.Debugging_Value_Reply reply = message.Payload as Commands.Debugging_Value_Reply;
                    if (reply != null)
                    {
                        vals[i++] = RuntimeValue.Convert(this, reply.m_values);
                    }
                }
            }

            return vals;
        }

        public async Task<RuntimeValue> GetArrayElementAsync(uint arrayReferenceId, uint index)
        {
            Commands.Debugging_Value_GetArray cmd = new Commands.Debugging_Value_GetArray();

            cmd.m_heapblock = arrayReferenceId;
            cmd.m_index = index;

            RuntimeValue rtv = await GetRuntimeValueAsync(Commands.c_Debugging_Value_GetArray, cmd).ConfigureAwait(false);

            if (rtv != null)
            {
                rtv.m_handle.m_arrayref_referenceID = arrayReferenceId;
                rtv.m_handle.m_arrayref_index = index;
            }

            return rtv;
        }

        internal async Task<bool> SetArrayElementAsync(uint heapblock, uint index, byte[] data)
        {
            Commands.Debugging_Value_SetArray cmd = new Commands.Debugging_Value_SetArray();

            cmd.m_heapblock = heapblock;
            cmd.m_index = index;

            data.CopyTo(cmd.m_value, 0);

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Value_SetArray, 0, cmd).ConfigureAwait(false));
        }

        public async Task<RuntimeValue> GetScratchPadValue(int index)
        {
            Commands.Debugging_Value_GetScratchPad cmd = new Commands.Debugging_Value_GetScratchPad();

            cmd.m_index = index;

            return await GetRuntimeValueAsync(Commands.c_Debugging_Value_GetScratchPad, cmd).ConfigureAwait(false);
        }

        public async Task<RuntimeValue> AllocateObjectAsync(int scratchPadLocation, uint td)
        {
            Commands.Debugging_Value_AllocateObject cmd = new Commands.Debugging_Value_AllocateObject();

            cmd.m_index = scratchPadLocation;
            cmd.m_td = td;

            return await GetRuntimeValueAsync(Commands.c_Debugging_Value_AllocateObject, cmd).ConfigureAwait(false);
        }

        public async Task<RuntimeValue> AllocateStringAsync(int scratchPadLocation, string val)
        {
            Commands.Debugging_Value_AllocateString cmd = new Commands.Debugging_Value_AllocateString();

            cmd.m_index = scratchPadLocation;
            cmd.m_size = (uint)Encoding.UTF8.GetByteCount(val);

            RuntimeValue rtv = await GetRuntimeValueAsync(Commands.c_Debugging_Value_AllocateString, cmd).ConfigureAwait(false);

            if (rtv != null)
            {
                await rtv.SetStringValueAsync(val).ConfigureAwait(false);
            }

            return rtv;
        }

        public async Task<RuntimeValue> AllocateArrayAsync(int scratchPadLocation, uint td, int depth, int numOfElements)
        {
            Commands.Debugging_Value_AllocateArray cmd = new Commands.Debugging_Value_AllocateArray();

            cmd.m_index = scratchPadLocation;
            cmd.m_td = td;
            cmd.m_depth = (uint)depth;
            cmd.m_numOfElements = (uint)numOfElements;

            return await GetRuntimeValueAsync(Commands.c_Debugging_Value_AllocateArray, cmd).ConfigureAwait(false);
        }

        public async Task<Commands.Debugging_Resolve_Type.Result> ResolveTypeAsync(uint td)
        {
            Commands.Debugging_Resolve_Type.Result result = (Commands.Debugging_Resolve_Type.Result)m_typeSysLookup.Lookup(TypeSysLookup.Type.Type, td);

            if (result == null)
            {
                Commands.Debugging_Resolve_Type cmd = new Commands.Debugging_Resolve_Type();

                cmd.m_td = td;

                IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Resolve_Type, 0, cmd).ConfigureAwait(false);

                if (reply != null)
                {
                    Commands.Debugging_Resolve_Type.Reply cmdReply = reply.Payload as Commands.Debugging_Resolve_Type.Reply;

                    if (cmdReply != null)
                    {
                        result = new Commands.Debugging_Resolve_Type.Result();

                        result.m_name = Commands.GetZeroTerminatedString(cmdReply.m_type, false);

                        m_typeSysLookup.Add(TypeSysLookup.Type.Type, td, result);
                    }
                }
            }

            return result;
        }

        public async Task<Commands.Debugging_Resolve_Method.Result> ResolveMethodAsync(uint md)
        {
            Commands.Debugging_Resolve_Method.Result result = (Commands.Debugging_Resolve_Method.Result)m_typeSysLookup.Lookup(TypeSysLookup.Type.Method, md);
            ;

            if (result == null)
            {
                Commands.Debugging_Resolve_Method cmd = new Commands.Debugging_Resolve_Method();

                cmd.m_md = md;

                IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Resolve_Method, 0, cmd).ConfigureAwait(false);

                if (reply != null)
                {
                    Commands.Debugging_Resolve_Method.Reply cmdReply = reply.Payload as Commands.Debugging_Resolve_Method.Reply;

                    if (cmdReply != null)
                    {
                        result = new Commands.Debugging_Resolve_Method.Result();

                        result.m_name = Commands.GetZeroTerminatedString(cmdReply.m_method, false);
                        result.m_td = cmdReply.m_td;

                        m_typeSysLookup.Add(TypeSysLookup.Type.Method, md, result);
                    }
                }
            }

            return result;
        }

        public async Task<Commands.Debugging_Resolve_Field.Result> ResolveFieldAsync(uint fd)
        {
            Commands.Debugging_Resolve_Field.Result result = (Commands.Debugging_Resolve_Field.Result)m_typeSysLookup.Lookup(TypeSysLookup.Type.Field, fd);
            ;

            if (result == null)
            {
                Commands.Debugging_Resolve_Field cmd = new Commands.Debugging_Resolve_Field();

                cmd.m_fd = fd;

                IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Resolve_Field, 0, cmd).ConfigureAwait(false);
                if (reply != null)
                {
                    Commands.Debugging_Resolve_Field.Reply cmdReply = reply.Payload as Commands.Debugging_Resolve_Field.Reply;

                    if (cmdReply != null)
                    {
                        result = new Commands.Debugging_Resolve_Field.Result();

                        result.m_name = Commands.GetZeroTerminatedString(cmdReply.m_name, false);
                        result.m_offset = cmdReply.m_offset;
                        result.m_td = cmdReply.m_td;

                        m_typeSysLookup.Add(TypeSysLookup.Type.Field, fd, result);
                    }
                }
            }

            return result;
        }

        public async Task<Commands.Debugging_Resolve_AppDomain.Reply> ResolveAppDomainAsync(uint appDomainID)
        {
            if (!Capabilities.AppDomains)
                return null;

            Commands.Debugging_Resolve_AppDomain cmd = new Commands.Debugging_Resolve_AppDomain();

            cmd.m_id = appDomainID;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Resolve_AppDomain, 0, cmd).ConfigureAwait(false);

            if (reply != null)
            {
                return reply.Payload as Commands.Debugging_Resolve_AppDomain.Reply;
            }

            return null;
        }

        public async Task<string> GetTypeNameAsync(uint td)
        {
            Commands.Debugging_Resolve_Type.Result resolvedType = await ResolveTypeAsync(td).ConfigureAwait(false);

            return (resolvedType != null) ? resolvedType.m_name : null;
        }

        public async Task<string> GetMethodNameAsync(uint md, bool fIncludeType)
        {
            Commands.Debugging_Resolve_Method.Result resolvedMethod = await ResolveMethodAsync(md).ConfigureAwait(false);
            string name = null;

            if (resolvedMethod != null)
            {
                if (fIncludeType)
                {
                    name = string.Format("{0}::{1}", await GetTypeNameAsync(resolvedMethod.m_td).ConfigureAwait(false), resolvedMethod.m_name);
                }
                else
                {
                    name = resolvedMethod.m_name;
                }
            }

            return name;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="fd"></param>
        /// <returns>Tuple with field name, td and offset.</returns>
        public async Task<Tuple<string, uint, uint>> GetFieldNameAsync(uint fd) 
        {
            Commands.Debugging_Resolve_Field.Result resolvedField = await ResolveFieldAsync(fd).ConfigureAwait(false);

            if (resolvedField != null)
            {
                return new Tuple<string, uint, uint>(resolvedField.m_name, resolvedField.m_td, resolvedField.m_offset);
            }

            return new Tuple<string, uint, uint>(null, 0, 0);
        }

        public async Task<uint> GetVirtualMethodAsync(uint md, RuntimeValue obj)
        {
            Commands.Debugging_Resolve_VirtualMethod cmd = new Commands.Debugging_Resolve_VirtualMethod();

            cmd.m_md = md;
            cmd.m_obj = obj.ReferenceId;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Resolve_VirtualMethod, 0, cmd).ConfigureAwait(false);

            if (reply != null)
            {
                Commands.Debugging_Resolve_VirtualMethod.Reply cmdReply = reply.Payload as Commands.Debugging_Resolve_VirtualMethod.Reply;

                if (cmdReply != null)
                {
                    return cmdReply.m_md;
                }
            }

            return 0;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>Tuple with widthInWords, heightInPixels, buffer and request result success.</returns>
        public async Task<Tuple<ushort, ushort, uint[], bool>> GetFrameBufferAsync()
        {
            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Lcd_GetFrame, 0, null).ConfigureAwait(false);
            if (reply != null)
            {
                Commands.Debugging_Lcd_GetFrame.Reply cmdReply = reply.Payload as Commands.Debugging_Lcd_GetFrame.Reply;

                if (cmdReply != null)
                {
                    return new Tuple<ushort, ushort, uint[], bool>(cmdReply.m_header.m_widthInWords, cmdReply.m_header.m_heightInPixels, cmdReply.m_data, true);
                }
            }

            return new Tuple<ushort, ushort, uint[], bool>(0, 0, null, false);
        }

        //private void Adjust1bppOrientation(uint[] buf)
        //{
        //    //CLR_GFX_Bitmap::AdjustBitOrientation
        //    //The TinyCLR treats 1bpp bitmaps reversed from Windows
        //    //And most likely every other 1bpp format as well
        //    byte[] reverseTable = new byte[]
        //    {
        //        0x00, 0x80, 0x40, 0xC0, 0x20, 0xA0, 0x60, 0xE0,
        //        0x10, 0x90, 0x50, 0xD0, 0x30, 0xB0, 0x70, 0xF0,
        //        0x08, 0x88, 0x48, 0xC8, 0x28, 0xA8, 0x68, 0xE8,
        //        0x18, 0x98, 0x58, 0xD8, 0x38, 0xB8, 0x78, 0xF8,
        //        0x04, 0x84, 0x44, 0xC4, 0x24, 0xA4, 0x64, 0xE4,
        //        0x14, 0x94, 0x54, 0xD4, 0x34, 0xB4, 0x74, 0xF4,
        //        0x0C, 0x8C, 0x4C, 0xCC, 0x2C, 0xAC, 0x6C, 0xEC,
        //        0x1C, 0x9C, 0x5C, 0xDC, 0x3C, 0xBC, 0x7C, 0xFC,
        //        0x02, 0x82, 0x42, 0xC2, 0x22, 0xA2, 0x62, 0xE2,
        //        0x12, 0x92, 0x52, 0xD2, 0x32, 0xB2, 0x72, 0xF2,
        //        0x0A, 0x8A, 0x4A, 0xCA, 0x2A, 0xAA, 0x6A, 0xEA,
        //        0x1A, 0x9A, 0x5A, 0xDA, 0x3A, 0xBA, 0x7A, 0xFA,
        //        0x06, 0x86, 0x46, 0xC6, 0x26, 0xA6, 0x66, 0xE6,
        //        0x16, 0x96, 0x56, 0xD6, 0x36, 0xB6, 0x76, 0xF6,
        //        0x0E, 0x8E, 0x4E, 0xCE, 0x2E, 0xAE, 0x6E, 0xEE,
        //        0x1E, 0x9E, 0x5E, 0xDE, 0x3E, 0xBE, 0x7E, 0xFE,
        //        0x01, 0x81, 0x41, 0xC1, 0x21, 0xA1, 0x61, 0xE1,
        //        0x11, 0x91, 0x51, 0xD1, 0x31, 0xB1, 0x71, 0xF1,
        //        0x09, 0x89, 0x49, 0xC9, 0x29, 0xA9, 0x69, 0xE9,
        //        0x19, 0x99, 0x59, 0xD9, 0x39, 0xB9, 0x79, 0xF9,
        //        0x05, 0x85, 0x45, 0xC5, 0x25, 0xA5, 0x65, 0xE5,
        //        0x15, 0x95, 0x55, 0xD5, 0x35, 0xB5, 0x75, 0xF5,
        //        0x0D, 0x8D, 0x4D, 0xCD, 0x2D, 0xAD, 0x6D, 0xED,
        //        0x1D, 0x9D, 0x5D, 0xDD, 0x3D, 0xBD, 0x7D, 0xFD,
        //        0x03, 0x83, 0x43, 0xC3, 0x23, 0xA3, 0x63, 0xE3,
        //        0x13, 0x93, 0x53, 0xD3, 0x33, 0xB3, 0x73, 0xF3,
        //        0x0B, 0x8B, 0x4B, 0xCB, 0x2B, 0xAB, 0x6B, 0xEB,
        //        0x1B, 0x9B, 0x5B, 0xDB, 0x3B, 0xBB, 0x7B, 0xFB,
        //        0x07, 0x87, 0x47, 0xC7, 0x27, 0xA7, 0x67, 0xE7,
        //        0x17, 0x97, 0x57, 0xD7, 0x37, 0xB7, 0x77, 0xF7,
        //        0x0F, 0x8F, 0x4F, 0xCF, 0x2F, 0xAF, 0x6F, 0xEF,
        //        0x1F, 0x9F, 0x5F, 0xDF, 0x3F, 0xBF, 0x7F,0xFF,
        //        };

        //    unsafe
        //    {
        //        fixed (uint* pbuf = buf)
        //        {
        //            byte* ptr = (byte*)pbuf;

        //            for (int i = buf.Length * 4; i > 0; i--)
        //            {
        //                *ptr = reverseTable[*ptr];
        //                ptr++;
        //            }
        //        }
        //    }
        //}

        //public Bitmap GetFrameBuffer()
        //{
        //    throw new NotImplementedException();

        //    //ushort widthInWords;
        //    //ushort heightInPixels;
        //    //uint[] buf;

        //    //Bitmap bmp = null;


        //    //PixelFormat pixelFormat = PixelFormat.DontCare;

        //    //if (GetFrameBuffer(out widthInWords, out heightInPixels, out buf))
        //    //{
        //    //    CLRCapabilities.LCDCapabilities lcdCaps = Capabilities.LCD;

        //    //    int pixelsPerWord = 32 / (int)lcdCaps.BitsPerPixel;

        //    //    Debug.Assert(heightInPixels == lcdCaps.Height);
        //    //    Debug.Assert(widthInWords == (lcdCaps.Width + pixelsPerWord - 1) / pixelsPerWord);

        //    //    Color[] colors = null;

        //    //    switch (lcdCaps.BitsPerPixel)
        //    //    {
        //    //        case 1:
        //    //            pixelFormat = PixelFormat.Format1bppIndexed;
        //    //            colors = new Color[] { Color.White, Color.Black };
        //    //            Adjust1bppOrientation(buf);
        //    //            break;
        //    //        case 4:
        //    //        case 8:
        //    //            //Not tested
        //    //            int cColors = 1 << (int)lcdCaps.BitsPerPixel;

        //    //            pixelFormat = (lcdCaps.BitsPerPixel == 4) ? PixelFormat.Format4bppIndexed : PixelFormat.Format8bppIndexed;

        //    //            colors = new Color[cColors];

        //    //            for (int i = 0; i < cColors; i++)
        //    //            {
        //    //                int intensity = 256 / cColors * i;
        //    //                colors[i] = Color.FromArgb(intensity, intensity, intensity);
        //    //            }

        //    //            break;
        //    //        case 16:
        //    //            pixelFormat = PixelFormat.Format16bppRgb565;
        //    //            break;
        //    //        default:
        //    //            Debug.Assert(false);
        //    //            return null;
        //    //    }

        //    //    BitmapData bitmapData = null;

        //    //    try
        //    //    {
        //    //        bmp = new Bitmap((int)lcdCaps.Width, (int)lcdCaps.Height, pixelFormat);
        //    //        Rectangle rect = new Rectangle(0, 0, (int)lcdCaps.Width, (int)lcdCaps.Height);

        //    //        if (colors != null)
        //    //        {
        //    //            ColorPalette palette = bmp.Palette;
        //    //            colors.CopyTo(palette.Entries, 0);
        //    //            bmp.Palette = palette;
        //    //        }

        //    //        bitmapData = bmp.LockBits(rect, ImageLockMode.WriteOnly, pixelFormat);
        //    //        IntPtr data = bitmapData.Scan0;

        //    //        unsafe
        //    //        {
        //    //            fixed (uint* pbuf = buf)
        //    //            {
        //    //                uint* src = (uint*)pbuf;
        //    //                uint* dst = (uint*)data.ToPointer();

        //    //                for (int i = buf.Length; i > 0; i--)
        //    //                {
        //    //                    *dst = *src;
        //    //                    dst++;
        //    //                    src++;
        //    //                }

        //    //            }
        //    //        }
        //    //    }

        //    //    finally
        //    //    {
        //    //        if (bitmapData != null)
        //    //        {
        //    //            bmp.UnlockBits(bitmapData);
        //    //        }
        //    //    }
        //    //}

        //    //return bmp;
        //}

        public async Task InjectButtonsAsync(uint pressed, uint released)
        {
            Commands.Debugging_Button_Inject cmd = new Commands.Debugging_Button_Inject();

            cmd.m_pressed = pressed;
            cmd.m_released = released;

            await PerformRequestAsync(Commands.c_Debugging_Button_Inject, 0, cmd).ConfigureAwait(false);
        }

        public async Task<List<ThreadStatus>> GetThreadsAsync()
        {
            List<ThreadStatus> threads = new List<ThreadStatus>();
            uint[] pids = await GetThreadListAsync().ConfigureAwait(false);

            if (pids != null)
            {
                for (int i = 0; i < pids.Length; i++)
                {
                    Commands.Debugging_Thread_Stack.Reply reply = await GetThreadStackAsync(pids[i]).ConfigureAwait(false);

                    if (reply != null)
                    {
                        int depth = reply.m_data.Length;
                        ThreadStatus ts = new ThreadStatus();

                        ts.m_pid = pids[i];
                        ts.m_status = reply.m_status;
                        ts.m_flags = reply.m_flags;
                        ts.m_calls = new string[depth];

                        for (int j = 0; j < depth; j++)
                        {
                            ts.m_calls[depth - 1 - j] = String.Format("{0} [IP:{1:X4}]", await GetMethodNameAsync(reply.m_data[j].m_md, true).ConfigureAwait(false), reply.m_data[j].m_IP);
                        }

                        threads.Add(ts);
                    }
                }

                return threads;
            }

            return null;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="entrypoint"></param>
        /// <param name="storageStart"></param>
        /// <param name="storageLength"></param>
        /// <returns>Tuple with entrypoint, storageStart, storageLength and request success</returns>
        public async Task<Tuple<uint, uint, uint, bool>> DeploymentGetStatusWithResultAsync()
        {
            Commands.Debugging_Deployment_Status.Reply status = await DeploymentGetStatusAsync();

            if (status != null)
            {
                return new Tuple<uint, uint, uint, bool>(status.m_entryPoint, status.m_storageStart, status.m_storageLength, true);
            }
            else
            {
                return new Tuple<uint, uint, uint, bool>(0, 0, 0, false);
            }
        }

        public async Task<Commands.Debugging_Deployment_Status.Reply> DeploymentGetStatusAsync()
        {
            Commands.Debugging_Deployment_Status cmd = new Commands.Debugging_Deployment_Status();
            Commands.Debugging_Deployment_Status.Reply cmdReply = null;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Deployment_Status, Flags.c_NoCaching, cmd, 2, 10000).ConfigureAwait(false);

            if (reply != null)
            {
                cmdReply = reply.Payload as Commands.Debugging_Deployment_Status.Reply;
            }

            return cmdReply;
        }

        public async Task<bool> Info_SetJMCAsyn(bool fJMC, ReflectionDefinition.Kind kind, uint index)
        {
            Commands.Debugging_Info_SetJMC cmd = new Commands.Debugging_Info_SetJMC();

            cmd.m_fIsJMC = (uint)(fJMC ? 1 : 0);
            cmd.m_kind = (uint)kind;
            cmd.m_raw = index;

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Info_SetJMC, 0, cmd).ConfigureAwait(false));
        }

        private async Task<bool> DeploymentExecuteIncrementalAsync(List<byte[]> assemblies, IProgress<string> progress)
        {
            Commands.Debugging_Deployment_Status.ReplyEx status = await DeploymentGetStatusAsync() as Commands.Debugging_Deployment_Status.ReplyEx;

            if (status == null)
            {
                return false;
            }

            List<Commands.Debugging_Deployment_Status.FlashSector> sectors = status.m_data;

            int iAssembly = 0;

            //The amount of bytes that the deployment will take
            int deployLength = 0;

            //Compute size of assemblies to deploy
            for (iAssembly = 0; iAssembly < assemblies.Count; iAssembly++)
            {
                byte[] assembly = assemblies[iAssembly];
                deployLength += assembly.Length;
            }

            if (deployLength > status.m_storageLength)
            {
                progress?.Report(string.Format("Deployment storage (size: {0} bytes) was not large enough to fit deployment assemblies (size: {1} bytes)", status.m_storageLength, deployLength));

                return false;
            }

            //Compute maximum sector size
            uint maxSectorSize = 0;

            for (int iSector = 0; iSector < sectors.Count; iSector++)
            {
                maxSectorSize = Math.Max(maxSectorSize, sectors[iSector].m_length);
            }

            //pre-allocate sector data, and a buffer to hold an empty sector's data
            byte[] sectorData = new byte[maxSectorSize];
            byte[] sectorDataErased = new byte[maxSectorSize];

            Debug.Assert(status.m_eraseWord == 0 || status.m_eraseWord == 0xffffffff);

            byte bErase = (status.m_eraseWord == 0) ? (byte)0 : (byte)0xff;
            if (bErase != 0)
            {
                //Fill in data for what an empty sector looks like
                for (int i = 0; i < maxSectorSize; i++)
                {
                    sectorDataErased[i] = bErase;
                }
            }

            int bytesDeployed = 0;

            //The assembly we are using
            iAssembly = 0;

            //byte index into the assembly remaining to deploy
            int iAssemblyIndex = 0;

            //deploy each sector, one at a time
            for (int iSector = 0; iSector < sectors.Count; iSector++)
            {
                Commands.Debugging_Deployment_Status.FlashSector sector = sectors[iSector];

                int cBytesLeftInSector = (int)sector.m_length;
                //byte index into the sector that we are deploying to.
                int iSectorIndex = 0;

                //fill sector with deployment data
                while (cBytesLeftInSector > 0 && iAssembly < assemblies.Count)
                {
                    byte[] assembly = assemblies[iAssembly];

                    int cBytesLeftInAssembly = assembly.Length - iAssemblyIndex;

                    //number of bytes from current assembly to deploy in this sector
                    int cBytes = Math.Min(cBytesLeftInSector, cBytesLeftInAssembly);

                    Array.Copy(assembly, (int)iAssemblyIndex, sectorData, iSectorIndex, cBytes);

                    cBytesLeftInSector -= cBytes;
                    iAssemblyIndex += cBytes;
                    iSectorIndex += cBytes;

                    //Is assembly finished?
                    if (iAssemblyIndex == assembly.Length)
                    {
                        //Next assembly
                        iAssembly++;
                        iAssemblyIndex = 0;

                        //If there is enough room to waste the remainder of this sector, do so
                        //to allow for incremental deployment, if this assembly changes for next deployment
                        if (deployLength + cBytesLeftInSector <= status.m_storageLength)
                        {
                            deployLength += cBytesLeftInSector;
                            break;
                        }
                    }
                }

                uint crc = Commands.Debugging_Deployment_Status.c_CRC_Erased_Sentinel;

                if (iSectorIndex > 0)
                {
                    //Fill in the rest with erased value
                    Array.Copy(sectorDataErased, iSectorIndex, sectorData, iSectorIndex, cBytesLeftInSector);

                    crc = CRC.ComputeCRC(sectorData, 0, (int)sector.m_length, 0);
                }

                //Has the data changed from what is in this sector
                if (sector.m_crc != crc)
                {
                    //Is the data not erased
                    if (sector.m_crc != Commands.Debugging_Deployment_Status.c_CRC_Erased_Sentinel)
                    {
                        if (!await EraseMemoryAsync(sector.m_start, sector.m_length))
                        {
                            progress?.Report((string.Format("FAILED to erase device memory @0x{0:X8} with Length=0x{1:X8}", sector.m_start, sector.m_length)));

                            return false;
                        }

#if DEBUG
                        Commands.Debugging_Deployment_Status.ReplyEx statusT = await DeploymentGetStatusAsync() as Commands.Debugging_Deployment_Status.ReplyEx;
                        Debug.Assert(statusT != null);
                        Debug.Assert(statusT.m_data[iSector].m_crc == Commands.Debugging_Deployment_Status.c_CRC_Erased_Sentinel);
#endif
                    }

                    //Is there anything to deploy
                    if (iSectorIndex > 0)
                    {
                        bytesDeployed += iSectorIndex;

                        if (!await WriteMemoryAsync(sector.m_start, sectorData, 0, (int)iSectorIndex))
                        {
                            progress?.Report((string.Format("FAILED to write device memory @0x{0:X8} with Length={1:X8}", sector.m_start, (int)iSectorIndex)));

                            return false;
                        }
#if DEBUG
                        Commands.Debugging_Deployment_Status.ReplyEx statusT = await DeploymentGetStatusAsync() as Commands.Debugging_Deployment_Status.ReplyEx;
                        Debug.Assert(statusT != null);
                        Debug.Assert(statusT.m_data[iSector].m_crc == crc);
                        //Assert the data we are deploying is not sentinel value
                        Debug.Assert(crc != Commands.Debugging_Deployment_Status.c_CRC_Erased_Sentinel);
#endif
                    }
                }
            }

            if (bytesDeployed == 0)
            {
                progress?.Report("All assemblies on the device are up to date.  No assembly deployment was necessary.");
            }
            else
            {
                progress?.Report(string.Format("Deploying assemblies for a total size of {0} bytes", bytesDeployed));
            }

            return true;
        }

        private async Task<bool> DeploymentExecuteFullAsync(List<byte[]> assemblies, IProgress<string> progress)
        {
            uint entrypoint;
            uint storageStart;
            uint storageLength;
            uint deployLength;
            byte[] closeHeader = new byte[8];

            // perform request
            var reply = await DeploymentGetStatusWithResultAsync();

            // check if request was successfully executed
            if (!reply.Item4)
            {
                return false;
            }

            // fill in the local properties with the result
            entrypoint = reply.Item1;
            storageStart = reply.Item2;
            storageLength = reply.Item3;

            if (storageLength == 0)
            {
                return false;
            }

            deployLength = (uint)closeHeader.Length;

            foreach (byte[] assembly in assemblies)
            {
                deployLength += (uint)assembly.Length;
            }

            progress?.Report(string.Format("Deploying assemblies for a total size of {0} bytes", deployLength));

            if (deployLength > storageLength)
            {
                return false;
            }

            if (!await EraseMemoryAsync(storageStart, deployLength))
            {
                return false;
            }

            foreach (byte[] assembly in assemblies)
            {
                //
                // Only word-aligned assemblies are allowed.
                //
                if (assembly.Length % 4 != 0)
                {
                    return false;
                }

                if (!await WriteMemoryAsync(storageStart, assembly))
                {
                    return false;
                }

                storageStart += (uint)assembly.Length;
            }

            if (!await WriteMemoryAsync(storageStart, closeHeader))
            {
                return false;
            }

            return true;
        }

        //public bool Deployment_Execute(ArrayList assemblies)
        //{
        //    return Deployment_Execute(assemblies, true, null);
        //}

        public async Task<bool> DeploymentExecuteAsync(List<byte[]> assemblies, bool fRebootAfterDeploy = true, IProgress<string> progress = null)
        {
            bool fDeployedOK = false;

            if (!await PauseExecutionAsync())
            {
                return false;
            }

            if (Capabilities.IncrementalDeployment)
            {
                progress?.Report("Incrementally deploying assemblies to device");

                fDeployedOK = await DeploymentExecuteIncrementalAsync(assemblies, progress);
            }
            else
            {
                progress?.Report("Deploying assemblies to device");

                fDeployedOK = await DeploymentExecuteFullAsync(assemblies, progress);
            }

            if (!fDeployedOK)
            {
                progress?.Report("Assemblies not successfully deployed to device.");
            }
            else
            {
                progress?.Report("Assemblies successfully deployed to device.");

                if (fRebootAfterDeploy)
                {

                    progress?.Report("Rebooting device...");

                    await RebootDeviceAsync(RebootOption.RebootClrOnly);
                }
            }

            return fDeployedOK;
        }

        public async Task<Tuple<uint, bool>> SetProfilingModeAsync(uint iSet, uint iReset)
        {
            Commands.Profiling_Command cmd = new Commands.Profiling_Command();
            cmd.m_command = Commands.Profiling_Command.c_Command_ChangeConditions;
            cmd.m_parm1 = iSet;
            cmd.m_parm2 = iReset;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Profiling_Command, 0, cmd).ConfigureAwait(false);
            if (reply != null)
            {
                Commands.Profiling_Command.Reply cmdReply = reply.Payload as Commands.Profiling_Command.Reply;

                if (cmdReply != null)
                {
                    return new Tuple<uint, bool>(cmdReply.m_raw, true);
                }
                else
                {
                    return new Tuple<uint, bool>(0, true);
                }
            }

            return new Tuple<uint, bool>(0, false);
        }

        public async Task<bool> FlushProfilingStreamAsync()
        {
            Commands.Profiling_Command cmd = new Commands.Profiling_Command();
            cmd.m_command = Commands.Profiling_Command.c_Command_FlushStream;
            await PerformRequestAsync(Commands.c_Profiling_Command, 0, cmd).ConfigureAwait(false);
            return true;
        }

        private async Task<IncomingMessage> DiscoverCLRCapabilityAsync(uint caps)
        {
            Commands.Debugging_Execution_QueryCLRCapabilities cmd = new Commands.Debugging_Execution_QueryCLRCapabilities();

            cmd.m_caps = caps;

            return await PerformRequestAsync(Commands.c_Debugging_Execution_QueryCLRCapabilities, 0, cmd, 5, 100).ConfigureAwait(false);
        }

        private async Task<uint> DiscoverCLRCapabilityUintAsync(uint caps)
        {
            uint ret = 0;

            IncomingMessage reply = await DiscoverCLRCapabilityAsync(caps).ConfigureAwait(false);

            if (reply != null)
            {
                Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_QueryCLRCapabilities.Reply;

                if (cmdReply != null && cmdReply.m_data != null && cmdReply.m_data.Length == 4)
                {
                    // can't use Converter because the deserialization of UInt32 is not supported
                    // replaced with a simple binary reader

                    MemoryStream stream = new MemoryStream(cmdReply.m_data);
                    BinaryReader reader = new BinaryReader(stream, Encoding.Unicode);

                    ret = reader.ReadUInt32();
                }
            }

            return ret;
        }

        private async Task<CLRCapabilities.Capability> DiscoverCLRCapabilityFlagsAsync()
        {
            Debug.WriteLine("DiscoverCLRCapability");

            return (CLRCapabilities.Capability) await DiscoverCLRCapabilityUintAsync(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityFlags).ConfigureAwait(false);
        }

        private async Task<CLRCapabilities.SoftwareVersionProperties> DiscoverSoftwareVersionPropertiesAsync()
        {
            Debug.WriteLine("DiscoverSoftwareVersionProperties");

            IncomingMessage reply = await DiscoverCLRCapabilityAsync(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilitySoftwareVersion).ConfigureAwait(false);

            Commands.Debugging_Execution_QueryCLRCapabilities.SoftwareVersion ver = new Commands.Debugging_Execution_QueryCLRCapabilities.SoftwareVersion();

            CLRCapabilities.SoftwareVersionProperties verCaps = new CLRCapabilities.SoftwareVersionProperties();

            if (reply != null)
            {
                Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_QueryCLRCapabilities.Reply;

                if (cmdReply != null && cmdReply.m_data != null)
                {
                    new Converter().Deserialize(ver, cmdReply.m_data);

                    verCaps = new CLRCapabilities.SoftwareVersionProperties(ver.m_buildDate, ver.m_compilerVersion);
                }
            }

            return verCaps;
        }

        private async Task<CLRCapabilities.LCDCapabilities> DiscoverCLRCapabilityLCDAsync()
        {
            Debug.WriteLine("DiscoverCLRCapabilityLCD");

            IncomingMessage reply = await DiscoverCLRCapabilityAsync(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityLCD).ConfigureAwait(false);

            Commands.Debugging_Execution_QueryCLRCapabilities.LCD lcd = new Commands.Debugging_Execution_QueryCLRCapabilities.LCD();

            CLRCapabilities.LCDCapabilities lcdCaps = new CLRCapabilities.LCDCapabilities();

            if (reply != null)
            {
                Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_QueryCLRCapabilities.Reply;

                if (cmdReply != null && cmdReply.m_data != null)
                {
                    new Converter().Deserialize(lcd, cmdReply.m_data);

                    lcdCaps = new CLRCapabilities.LCDCapabilities(lcd.m_width, lcd.m_height, lcd.m_bpp);
                }
            }

            return lcdCaps;
        }

        private async Task<CLRCapabilities.HalSystemInfoProperties> DiscoverHalSystemInfoPropertiesAsync()
        {
            Debug.WriteLine("DiscoverHalSystemInfoProperties");

            IncomingMessage reply = await DiscoverCLRCapabilityAsync(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityHalSystemInfo).ConfigureAwait(false);

            Commands.Debugging_Execution_QueryCLRCapabilities.HalSystemInfo halSystemInfo = new Commands.Debugging_Execution_QueryCLRCapabilities.HalSystemInfo();

            CLRCapabilities.HalSystemInfoProperties halProps = new CLRCapabilities.HalSystemInfoProperties();

            if (reply != null)
            {
                Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_QueryCLRCapabilities.Reply;

                if (cmdReply != null && cmdReply.m_data != null)
                {
                    new Converter().Deserialize(halSystemInfo, cmdReply.m_data);

                    halProps = new CLRCapabilities.HalSystemInfoProperties(
                                    halSystemInfo.m_releaseInfo.Version, halSystemInfo.m_releaseInfo.Info,
                                    halSystemInfo.m_OemModelInfo.OEM, halSystemInfo.m_OemModelInfo.Model, halSystemInfo.m_OemModelInfo.SKU,
                                    halSystemInfo.m_OemSerialNumbers.module_serial_number, halSystemInfo.m_OemSerialNumbers.system_serial_number
                                    );
                }
            }

            return halProps;
        }

        private async Task<CLRCapabilities.ClrInfoProperties> DiscoverClrInfoPropertiesAsync()
        {
            Debug.WriteLine("DiscoverClrInfoProperties");

            IncomingMessage reply = await DiscoverCLRCapabilityAsync(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityClrInfo).ConfigureAwait(false);

            Commands.Debugging_Execution_QueryCLRCapabilities.ClrInfo clrInfo = new Commands.Debugging_Execution_QueryCLRCapabilities.ClrInfo();

            CLRCapabilities.ClrInfoProperties clrInfoProps = new CLRCapabilities.ClrInfoProperties();

            if (reply != null)
            {
                Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_QueryCLRCapabilities.Reply;

                if (cmdReply != null && cmdReply.m_data != null)
                {
                    new Converter().Deserialize(clrInfo, cmdReply.m_data);

                    clrInfoProps = new CLRCapabilities.ClrInfoProperties(clrInfo.m_clrReleaseInfo.Version, clrInfo.m_clrReleaseInfo.Info, clrInfo.m_TargetFrameworkVersion.Version);
                }
            }

            return clrInfoProps;
        }

        private async Task<CLRCapabilities.SolutionInfoProperties> DiscoverSolutionInfoPropertiesAsync()
        {
            Debug.WriteLine("==============================");
            Debug.WriteLine("DiscoverSolutionInfoProperties");

            IncomingMessage reply = await DiscoverCLRCapabilityAsync(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilitySolutionReleaseInfo).ConfigureAwait(false);

            ReleaseInfo solutionInfo = new ReleaseInfo();

            CLRCapabilities.SolutionInfoProperties solInfProps = new CLRCapabilities.SolutionInfoProperties();

            if (reply != null)
            {
                Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_QueryCLRCapabilities.Reply;

                if (cmdReply != null && cmdReply.m_data != null)
                {
                    new Converter().Deserialize(solutionInfo, cmdReply.m_data);

                    solInfProps = new CLRCapabilities.SolutionInfoProperties(solutionInfo.Version, solutionInfo.Info);
                }
            }

            return solInfProps;
        }

        private async Task<CLRCapabilities> DiscoverCLRCapabilitiesAsync()
        {
            var clrFlags = await DiscoverCLRCapabilityFlagsAsync().ConfigureAwait(false);
            var clrLcd = await DiscoverCLRCapabilityLCDAsync().ConfigureAwait(false);
            var softwareVersion = await DiscoverSoftwareVersionPropertiesAsync().ConfigureAwait(false);
            var halSysInfo = await DiscoverHalSystemInfoPropertiesAsync().ConfigureAwait(false);
            var clrInfo = await DiscoverClrInfoPropertiesAsync().ConfigureAwait(false);
            var solutionInfo = await DiscoverSolutionInfoPropertiesAsync().ConfigureAwait(false);

            return new CLRCapabilities(clrFlags, clrLcd, softwareVersion, halSysInfo, clrInfo, solutionInfo);
        }

        #endregion

    }
}
