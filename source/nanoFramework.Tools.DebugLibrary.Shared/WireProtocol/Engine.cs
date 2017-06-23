//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Storage.Streams;

namespace nanoFramework.Tools.Debugger
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

        CancellationTokenSource noiseHandlingCancellation = new CancellationTokenSource();

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
        internal INanoDevice Device;

        public Engine(IPort pd, INanoDevice device)
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
                    m_notifyNoise.WaitHandle.WaitOne(250);

                    // check for cancelation request
                    if (noiseHandlingCancellation.IsCancellationRequested)
                    {
                        // cancellation requested
                        return;
                    }

                    while ((read = m_notifyNoise.Available) > 0)
                    {
                        byte[] buffer = new byte[m_notifyNoise.Available];

                        m_notifyNoise.Read(buffer, 0, buffer.Length);

                        if (SpuriousCharactersReceived != null)
                        {
                            SpuriousCharactersReceived.Invoke(this, new StringEventArgs(UTF8Encoding.UTF8.GetString(buffer, 0, buffer.Length)));
                        }
                    }

                }
            }, noiseHandlingCancellation.Token);

        }

        private void InitializeLocal(IPort pd, INanoDevice device)
        {
            m_portDefinition = pd;
            m_ctrl = new Controller(Packet.MARKER_PACKET_V1, this);

            Device = device;

            Initialize();
        }

        public CLRCapabilities Capabilities { get; internal set; }

        public bool IsConnected { get; internal set; }

        public ConnectionSource ConnectionSource { get; internal set; }

        public bool IsConnectedTonanoCLR
        {
            get { return ConnectionSource == ConnectionSource.NanoCLR; }
        }

        public bool IsTargetBigEndian { get; internal set; }

        public async ValueTask<bool> ConnectAsync(int retries, int timeout, bool force = false, ConnectionSource connectionSource = ConnectionSource.Unknown)
        {
            if (force || IsConnected == false)
            {
                // connect to device 
                if (await Device.ConnectAsync())
                {

                    Commands.Monitor_Ping cmd = new Commands.Monitor_Ping();

                    cmd.m_source = Commands.Monitor_Ping.c_Ping_Source_Host;
                    //cmd.m_dbg_flags = (m_stopDebuggerOnConnect ? Commands.Monitor_Ping.c_Ping_DbgFlag_Stop : 0);

                    IncomingMessage msg = await PerformRequestAsync(Commands.c_Monitor_Ping, Flags.c_NoCaching, cmd, retries, timeout);

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

                    ConnectionSource = (reply == null || reply.m_source == Commands.Monitor_Ping.c_Ping_Source_NanoCLR) ? ConnectionSource.NanoCLR : ConnectionSource.NanoBooter;

                    if (m_silent)
                    {
                        await SetExecutionModeAsync(Commands.Debugging_Execution_ChangeConditions.c_fDebugger_Quiet, 0);
                    }

                    // resume execution for older clients, since server tools no longer do this.
                    if (!m_stopDebuggerOnConnect && (msg != null && msg.Payload == null))
                    {
                        await ResumeExecutionAsync();
                    }
                }
            }

            if ((force || Capabilities.IsUnknown) && ConnectionSource == ConnectionSource.NanoCLR)
            {
                CancellationTokenSource cancellationTSource = new CancellationTokenSource();

                Capabilities = await DiscoverCLRCapabilitiesAsync(cancellationTSource.Token);
                m_ctrl.Capabilities = Capabilities;
            }

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

                Debug.WriteLine("Device disconnected");
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

                    // kill listener for spurious chars task
                    noiseHandlingCancellation.Cancel();

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
            await semaphore.WaitAsync();

            //// check for cancelation request
            //if (cancellationToken.IsCancellationRequested)
            //{
            //    // cancellation requested
            //    Debug.WriteLine("cancelation requested");
            //    return null;
            //}

            try
            {
                //Debug.WriteLine("_________________________________________________________");
                //Debug.WriteLine("Executing " + DebuggerEventSource.GetCommandName(command));
                //Debug.WriteLine("_________________________________________________________");

                // create message
                OutgoingMessage message = new OutgoingMessage(m_ctrl, CreateConverter(), command, flags, payload);

                // create request 
                Request request = new Request(m_ctrl, message, retries, timeout, null);

                return await request.PerformRequestAsync();
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async Task<IncomingMessage> PerformRequestAsync(OutgoingMessage message, CancellationToken cancellationToken, int retries = 3, int timeout = 500)
        {
            await semaphore.WaitAsync();

            try
            {

                // create request 
                Request request = new Request(m_ctrl, message, retries, timeout, null);

                return await request.PerformRequestAsync();
            }
            finally
            {
                semaphore.Release();
            }
        }

        private async Task<List<IncomingMessage>> PerformRequestBatchAsync(List<OutgoingMessage> messages, CancellationToken cancellationToken, int retries = 3, int timeout = 1000)
        {
            List<IncomingMessage> replies = new List<IncomingMessage>();
            List<Request> requests = new List<Request>();

            foreach (OutgoingMessage message in messages)
            {
                // continue execution only if cancelation was NOT request
                if (!cancellationToken.IsCancellationRequested)
                {
                    replies.Add(await PerformRequestAsync(message, cancellationToken, retries, timeout));
                }
                else
                {
                    break;
                }
            }

            return replies;
        }

        public async Task<Commands.Monitor_Ping.Reply> GetConnectionSourceAsync()
        {
            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_Ping, 0, null, 2, 500);

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
            return await m_portDefinition.SendBufferAsync(buffer, waiTimeout, cancellationToken);
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

        public async Task<byte[]> ReadBufferAsync(uint bytesToRead, TimeSpan waitTimeout, CancellationToken cancellationToken)
        {
            return await m_portDefinition.ReadBufferAsync(bytesToRead, waitTimeout, cancellationToken);
        }

        private OutgoingMessage CreateMessage(uint cmd, uint flags, object payload)
        {
            return new OutgoingMessage(m_ctrl, CreateConverter(), cmd, flags, payload);
        }


        #region Commands implementation

        public async Task<List<Commands.Monitor_MemoryMap.Range>> GetMemoryMapAsync()
        {
            Commands.Monitor_MemoryMap cmd = new Commands.Monitor_MemoryMap();

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_MemoryMap, 0, cmd);

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

            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_DeploymentMap, 0, cmd, 2, 10000);

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
            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_OemInfo, 0, null, 2, 1000);

            if (reply != null)
            {
                return reply.Payload as Commands.Monitor_OemInfo.Reply;
            }

            return null;
        }

        public async Task<List<Commands.Monitor_FlashSectorMap.FlashSectorData>> GetFlashSectorMapAsync()
        {
            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_FlashSectorMap, 0, null, 1, 4000);

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

        private async Task<(byte[] buffer, bool success)> ReadMemoryAsync(uint address, uint length, uint offset)
        {
            byte[] buffer = new byte[length];

            while (length > 0)
            {
                Commands.Monitor_ReadMemory cmd = new Commands.Monitor_ReadMemory();

                cmd.m_address = address;
                cmd.m_length = length;

                IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_ReadMemory, 0, cmd);
                if (reply == null)
                {
                    return (new byte[0], false);
                }

                Commands.Monitor_ReadMemory.Reply cmdReply = reply.Payload as Commands.Monitor_ReadMemory.Reply;

                if (cmdReply == null || cmdReply.m_data == null)
                {
                    return (new byte[0], false);
                }

                uint actualLength = Math.Min((uint)cmdReply.m_data.Length, length);

                Array.Copy(cmdReply.m_data, 0, buffer, (int)offset, (int)actualLength);

                address += actualLength;
                length -= actualLength;
                offset += actualLength;
            }

            return (new byte[0], true);
        }

        public async Task<(byte[] buffer, bool success)> ReadMemoryAsync(uint address, uint length)
        {
            return await ReadMemoryAsync(address, length, 0);
        }

        public async Task<(uint errorCode, bool success)> WriteMemoryAsync(uint address, byte[] buf, int offset, int length)
        {
            Debug.WriteLine($"Write memory operation. Start address { address.ToString("X8") }, lenght {length}");

            int count = length;
            int pos = offset;

            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            while (count > 0)
            {
                Commands.Monitor_WriteMemory cmd = new Commands.Monitor_WriteMemory();
                int len = Math.Min(1024, count);

                cmd.PrepareForSend(address, buf, pos, len);

                DebuggerEventSource.Log.EngineWriteMemory(address, len);

                Debug.WriteLine($"Sending {len} bytes to address { address.ToString("X8") }, {count} remaining...");

                IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_WriteMemory, 0, cmd, 0, 2000);

                Commands.Monitor_WriteMemory.Reply cmdReply = reply.Payload as Commands.Monitor_WriteMemory.Reply;

                if (!IncomingMessage.IsPositiveAcknowledge(reply))
                {
                    return (cmdReply.ErrorCode, false);
                }

                address += (uint)len;
                count -= len;
                pos += len;
            }

            return (0, true);
        }

        public async Task<(uint errorCode, bool success)> WriteMemoryAsync(uint address, byte[] buf)
        {
            return await WriteMemoryAsync(address, buf, 0, buf.Length);
        }

        public async Task<(uint errorCode, bool success)> EraseMemoryAsync(uint address, uint length)
        {
            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            DebuggerEventSource.Log.EngineEraseMemory(address, length);

            var cmd = new Commands.Monitor_EraseMemory
            {
                m_address = address,
                m_length = length
            };

            // typical max Flash erase times for STM32 parts with PSIZE set to 16bits are:
            // 16kB sector:  600ms  >> 38ms/kB
            // 64kB sector:  1400ms >> 22ms/kB
            // 128kB sector: 2600ms >> 21ms/kB

            // the erase memory command isn't aware of the sector(s) size it will end up erasing so we have to do an educated guess on how long that will take
            // considering the worst case timming which is the erase of the smallest sector.

            // default timeout is 0ms
            var timeout = 0;

            if (length <= (16 * 1024))
            {
                // timeout for 16kB sector
                timeout = 600;
            }
            else if (length <= (64 * 1024))
            {
                // timeout for 64kB sector
                timeout = 1400;
            }
            else if (length <= (128 * 1024))
            {
                // timeout for 128kB sector
                timeout = 2600;
            }
            else
            {
                // timeout for anything above 128kB (multiple sectors)
                timeout = (int)(length / (16 * 1024)) * 600 + 500;
            }

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_EraseMemory, 0, cmd, 0, timeout);

            Commands.Monitor_EraseMemory.Reply cmdReply = reply.Payload as Commands.Monitor_EraseMemory.Reply;

            return (cmdReply.ErrorCode, IncomingMessage.IsPositiveAcknowledge(reply));
        }

        public async Task<bool> ExecuteMemoryAsync(uint address)
        {
            Commands.Monitor_Execute cmd = new Commands.Monitor_Execute();

            cmd.m_address = address;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Monitor_Execute, 0, cmd);

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

                await PerformRequestAsync(Commands.c_Monitor_Reboot, 0, cmd);

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
            if (!await ConnectAsync(m_RebootTime.Retries, m_RebootTime.WaitMs(fSoftReboot), true, ConnectionSource.Unknown))
            {
                if (m_fThrowOnCommunicationFailure)
                {
                    throw new Exception("Could not reconnect to nanoCLR");
                }
                return false;
            }

            return true;
        }

        public async Task<uint> GetExecutionBasePtrAsync()
        {
            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Execution_BasePtr, 0, null);
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

        public async Task<(uint currentExecutionMode, bool success)> SetExecutionModeAsync(uint iSet, uint iReset)
        {
            Commands.Debugging_Execution_ChangeConditions cmd = new Commands.Debugging_Execution_ChangeConditions();

            cmd.m_set = iSet;
            cmd.m_reset = iReset;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Execution_ChangeConditions, Flags.c_NoCaching, cmd);
            if (reply != null)
            {
                Commands.Debugging_Execution_ChangeConditions.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_ChangeConditions.Reply;

                if (cmdReply != null)
                {
                    return (cmdReply.m_current, true);
                }
                else
                {
                    return (0, true);
                }
            }

            return (0, false);
        }

        public async Task<bool> PauseExecutionAsync()
        {
            var ret = await SetExecutionModeAsync(Commands.Debugging_Execution_ChangeConditions.c_Stopped, 0);

            return ret.Item2;
        }

        public async Task<bool> ResumeExecutionAsync()
        {
            var ret = await SetExecutionModeAsync(0, Commands.Debugging_Execution_ChangeConditions.c_Stopped);

            return ret.Item2;
        }

        public async Task<bool> SetCurrentAppDomainAsync(uint id)
        {
            Commands.Debugging_Execution_SetCurrentAppDomain cmd = new Commands.Debugging_Execution_SetCurrentAppDomain();

            cmd.m_id = id;

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Execution_SetCurrentAppDomain, 0, cmd));
        }

        public async Task<bool> SetBreakpointsAsync(Commands.Debugging_Execution_BreakpointDef[] breakpoints)
        {
            Commands.Debugging_Execution_Breakpoints cmd = new Commands.Debugging_Execution_Breakpoints();

            cmd.m_data = breakpoints;

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Execution_Breakpoints, 0, cmd));
        }

        public async Task<Commands.Debugging_Execution_BreakpointDef> GetBreakpointStatusAsync()
        {
            Commands.Debugging_Execution_BreakpointStatus cmd = new Commands.Debugging_Execution_BreakpointStatus();

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Execution_BreakpointStatus, 0, cmd);

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

            return await PerformRequestAsync(Commands.c_Debugging_Execution_SecurityKey, 0, cmd) != null;
        }

        public async Task<bool> UnlockDeviceAsync(byte[] blob)
        {
            Commands.Debugging_Execution_Unlock cmd = new Commands.Debugging_Execution_Unlock();

            Array.Copy(blob, 0, cmd.m_command, 0, 128);
            Array.Copy(blob, 128, cmd.m_hash, 0, 128);

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Execution_Unlock, 0, cmd));
        }

        public async Task<(uint address, bool success)> AllocateMemoryAsync(uint size)
        {
            Commands.Debugging_Execution_Allocate cmd = new Commands.Debugging_Execution_Allocate();

            cmd.m_size = size;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Execution_Allocate, 0, cmd);
            if (reply != null)
            {
                Commands.Debugging_Execution_Allocate.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_Allocate.Reply;

                if (cmdReply != null)
                {
                    return (cmdReply.m_address, true);
                }
            }

            return (0, false);
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
            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            Commands.Debugging_UpgradeToSsl cmd = new Commands.Debugging_UpgradeToSsl();

            cmd.m_flags = 0;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_UpgradeToSsl, Flags.c_NoCaching, cmd, 2, 5000);

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

            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_MFUpdate_Start, Flags.c_NoCaching, cmd, 2, 5000);

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

        public async Task<(byte[] response, bool success)> UpdateAuthCommandAsync(int updateHandle, uint authCommand, byte[] commandArgs)
        {
            Commands.Debugging_MFUpdate_AuthCommand cmd = new Commands.Debugging_MFUpdate_AuthCommand();

            if (commandArgs == null)
            {
                commandArgs = new byte[0];
            }

            cmd.m_updateHandle = updateHandle;
            cmd.m_authCommand = authCommand;
            cmd.m_authArgs = commandArgs;
            cmd.m_authArgsSize = (uint)commandArgs.Length;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_MFUpdate_AuthCmd, Flags.c_NoCaching, cmd);

            if (reply != null)
            {
                Commands.Debugging_MFUpdate_AuthCommand.Reply cmdReply = reply.Payload as Commands.Debugging_MFUpdate_AuthCommand.Reply;

                if (cmdReply != null && cmdReply.m_success != 0)
                {
                    if (cmdReply.m_responseSize > 0)
                    {
                        byte[] response = new byte[4];
                        Array.Copy(cmdReply.m_response, response, Math.Min(response.Length, (int)cmdReply.m_responseSize));

                        return (response, true);
                    }
                }
            }

            return (new byte[4], true);
        }

        public async Task<bool> UpdateAuthenticateAsync(int updateHandle, byte[] authenticationData)
        {
            Commands.Debugging_MFUpdate_Authenticate cmd = new Commands.Debugging_MFUpdate_Authenticate();

            cmd.m_updateHandle = updateHandle;
            cmd.PrepareForSend(authenticationData);

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_MFUpdate_Authenticate, Flags.c_NoCaching, cmd);

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

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_MFUpdate_GetMissingPkts, Flags.c_NoCaching, cmd);

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
                await UpdateGetMissingPacketsAsync(updateHandle);
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

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_MFUpdate_AddPacket, Flags.c_NoCaching, cmd);
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

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_MFUpdate_Install, Flags.c_NoCaching, cmd);

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
            return await CreateThreadAsync(methodIndex, scratchPadLocation, 0);
        }

        public async Task<uint> CreateThreadAsync(uint methodIndex, int scratchPadLocation, uint pid)
        {
            if (Capabilities.ThreadCreateEx)
            {
                Commands.Debugging_Thread_CreateEx cmd = new Commands.Debugging_Thread_CreateEx();

                cmd.m_md = methodIndex;
                cmd.m_scratchPad = scratchPadLocation;
                cmd.m_pid = pid;

                IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Thread_CreateEx, 0, cmd);

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
            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Thread_List, 0, null);

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

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Thread_Stack, 0, cmd);

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

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Thread_Kill, 0, cmd);

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

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Thread_Suspend, 0, cmd));
        }

        public async Task<bool> ResumeThreadAsync(uint pid)
        {
            Commands.Debugging_Thread_Resume cmd = new Commands.Debugging_Thread_Resume();

            cmd.m_pid = pid;

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Thread_Resume, 0, cmd));
        }

        public async Task<RuntimeValue> GetThreadException(uint pid)
        {
            Commands.Debugging_Thread_GetException cmd = new Commands.Debugging_Thread_GetException();

            cmd.m_pid = pid;

            return await GetRuntimeValueAsync(Commands.c_Debugging_Thread_GetException, cmd);
        }

        public async Task<RuntimeValue> GetThreadAsync(uint pid)
        {
            Commands.Debugging_Thread_Get cmd = new Commands.Debugging_Thread_Get();

            cmd.m_pid = pid;

            return await GetRuntimeValueAsync(Commands.c_Debugging_Thread_Get, cmd);
        }

        public async Task<bool> UnwindThreadAsync(uint pid, uint depth)
        {
            Commands.Debugging_Thread_Unwind cmd = new Commands.Debugging_Thread_Unwind();

            cmd.m_pid = pid;
            cmd.m_depth = depth;

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Thread_Unwind, 0, cmd));
        }

        public async Task<bool> SetIPOfStackFrameAsync(uint pid, uint depth, uint IP, uint depthOfEvalStack)
        {
            Commands.Debugging_Stack_SetIP cmd = new Commands.Debugging_Stack_SetIP();

            cmd.m_pid = pid;
            cmd.m_depth = depth;

            cmd.m_IP = IP;
            cmd.m_depthOfEvalStack = depthOfEvalStack;

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Stack_SetIP, 0, cmd));
        }

        public async Task<Commands.Debugging_Stack_Info.Reply> GetStackInfoAsync(uint pid, uint depth)
        {
            Commands.Debugging_Stack_Info cmd = new Commands.Debugging_Stack_Info();

            cmd.m_pid = pid;
            cmd.m_depth = depth;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Stack_Info, 0, cmd);

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

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_TypeSys_AppDomains, 0, cmd);

            if (reply != null)
            {
                return reply.Payload as Commands.Debugging_TypeSys_AppDomains.Reply;
            }

            return null;
        }

        public async Task<Commands.Debugging_TypeSys_Assemblies.Reply> GetAssembliesAsync(CancellationToken cancellationToken)
        {
            Commands.Debugging_TypeSys_Assemblies cmd = new Commands.Debugging_TypeSys_Assemblies();

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_TypeSys_Assemblies, 0, cmd);

            if (reply != null)
            {
                return reply.Payload as Commands.Debugging_TypeSys_Assemblies.Reply;
            }

            return null;
        }

        public async Task<List<Commands.DebuggingResolveAssembly>> ResolveAllAssembliesAsync(CancellationToken cancellationToken)
        {
            Commands.Debugging_TypeSys_Assemblies.Reply assemblies = await GetAssembliesAsync(cancellationToken);
            List<Commands.DebuggingResolveAssembly> resolveAssemblies = new List<Commands.DebuggingResolveAssembly>();

            if (assemblies == null || assemblies.Data == null)
            {
                resolveAssemblies = new List<Commands.DebuggingResolveAssembly>();
            }
            else
            {
                List<OutgoingMessage> requests = new List<OutgoingMessage>();

                foreach (uint iAssembly in assemblies.Data)
                {
                    Commands.DebuggingResolveAssembly cmd = new Commands.DebuggingResolveAssembly()
                    {
                        Idx = iAssembly
                    };

                    requests.Add(CreateMessage(Commands.c_Debugging_Resolve_Assembly, 0, cmd));
                }

                List<IncomingMessage> replies = await PerformRequestBatchAsync(requests, cancellationToken);

                foreach (IncomingMessage message in replies)
                {
                    // reply is a match for request which m_seq is same as reply m_seqReply
                    resolveAssemblies.Add(requests.Find(req => req.Header.Seq == message.Header.SeqReply).Payload as Commands.DebuggingResolveAssembly);
                    resolveAssemblies[resolveAssemblies.Count - 1].Result = message.Payload as Commands.DebuggingResolveAssembly.Reply;
                }
            }

            return resolveAssemblies;
        }

        public async Task<Commands.DebuggingResolveAssembly.Reply> ResolveAssemblyAsync(uint idx)
        {
            Commands.DebuggingResolveAssembly cmd = new Commands.DebuggingResolveAssembly();

            cmd.Idx = idx;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Resolve_Assembly, 0, cmd);

            if (reply != null)
            {
                return reply.Payload as Commands.DebuggingResolveAssembly.Reply;
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
        public async Task<(uint numOfArguments, uint numOfLocals, uint depthOfEvalStack, bool success)> GetStackFrameInfoAsync(uint pid, uint depth)
        {
            Commands.Debugging_Stack_Info.Reply reply = await GetStackInfoAsync(pid, depth);

            if (reply == null)
            {
                return (0, 0, 0, false);
            }

            return (reply.m_numOfArguments, reply.m_numOfLocals, reply.m_depthOfEvalStack, true);
        }

        private async Task<RuntimeValue> GetRuntimeValueAsync(uint msg, object cmd)
        {
            IncomingMessage reply = await PerformRequestAsync(msg, 0, cmd);

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

            return await GetRuntimeValueAsync(Commands.c_Debugging_Value_GetField, cmd);
        }

        public async Task<RuntimeValue> GetStaticFieldValueAsync(uint fd)
        {
            return await GetFieldValueAsync(null, 0, fd);
        }

        internal async Task<RuntimeValue> AssignRuntimeValueAsync(uint heapblockSrc, uint heapblockDst)
        {
            Commands.Debugging_Value_Assign cmd = new Commands.Debugging_Value_Assign();

            cmd.m_heapblockSrc = heapblockSrc;
            cmd.m_heapblockDst = heapblockDst;

            return await GetRuntimeValueAsync(Commands.c_Debugging_Value_Assign, cmd);
        }

        internal async Task<bool> SetBlockAsync(uint heapblock, uint dt, byte[] data)
        {
            Commands.Debugging_Value_SetBlock setBlock = new Commands.Debugging_Value_SetBlock();

            setBlock.m_heapblock = heapblock;
            setBlock.m_dt = dt;

            data.CopyTo(setBlock.m_value, 0);

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Value_SetBlock, 0, setBlock));
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

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Value_ResizeScratchPad, 0, cmd));
        }

        public async Task<RuntimeValue> GetStackFrameValueAsync(uint pid, uint depth, StackValueKind kind, uint index, CancellationToken cancellationToken)
        {
            OutgoingMessage cmd = CreateMessage_GetValue_Stack(pid, depth, kind, index);

            IncomingMessage reply = await PerformRequestAsync(cmd, cancellationToken, 10, 200);

            if (reply != null)
            {
                Commands.Debugging_Value_Reply cmdReply = reply.Payload as Commands.Debugging_Value_Reply;

                return RuntimeValue.Convert(this, cmdReply.m_values);
            }

            return null;
        }

        public async Task<RuntimeValue[]> GetStackFrameValueAllAsync(uint pid, uint depth, uint cValues, StackValueKind kind, CancellationToken cancellationToken)
        {
            List<OutgoingMessage> cmds = new List<OutgoingMessage>();
            RuntimeValue[] vals = null;
            uint i = 0;

            for (i = 0; i < cValues; i++)
            {
                cmds.Add(CreateMessage_GetValue_Stack(pid, depth, kind, i));
            }

            List<IncomingMessage> replies = await PerformRequestBatchAsync(cmds, cancellationToken);

            if (replies != null)
            {
                vals = new RuntimeValue[cValues];

                foreach (IncomingMessage message in replies)
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

            RuntimeValue rtv = await GetRuntimeValueAsync(Commands.c_Debugging_Value_GetArray, cmd);

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

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Value_SetArray, 0, cmd));
        }

        public async Task<RuntimeValue> GetScratchPadValue(int index)
        {
            Commands.Debugging_Value_GetScratchPad cmd = new Commands.Debugging_Value_GetScratchPad();

            cmd.m_index = index;

            return await GetRuntimeValueAsync(Commands.c_Debugging_Value_GetScratchPad, cmd);
        }

        public async Task<RuntimeValue> AllocateObjectAsync(int scratchPadLocation, uint td)
        {
            Commands.Debugging_Value_AllocateObject cmd = new Commands.Debugging_Value_AllocateObject();

            cmd.m_index = scratchPadLocation;
            cmd.m_td = td;

            return await GetRuntimeValueAsync(Commands.c_Debugging_Value_AllocateObject, cmd);
        }

        public async Task<RuntimeValue> AllocateStringAsync(int scratchPadLocation, string val)
        {
            Commands.Debugging_Value_AllocateString cmd = new Commands.Debugging_Value_AllocateString();

            cmd.m_index = scratchPadLocation;
            cmd.m_size = (uint)Encoding.UTF8.GetByteCount(val);

            RuntimeValue rtv = await GetRuntimeValueAsync(Commands.c_Debugging_Value_AllocateString, cmd);

            if (rtv != null)
            {
                await rtv.SetStringValueAsync(val);
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

            return await GetRuntimeValueAsync(Commands.c_Debugging_Value_AllocateArray, cmd);
        }

        public async Task<Commands.Debugging_Resolve_Type.Result> ResolveTypeAsync(uint td)
        {
            Commands.Debugging_Resolve_Type.Result result = (Commands.Debugging_Resolve_Type.Result)m_typeSysLookup.Lookup(TypeSysLookup.Type.Type, td);

            if (result == null)
            {
                Commands.Debugging_Resolve_Type cmd = new Commands.Debugging_Resolve_Type();

                cmd.m_td = td;

                IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Resolve_Type, 0, cmd);

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

                IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Resolve_Method, 0, cmd);

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

                IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Resolve_Field, 0, cmd);
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

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Resolve_AppDomain, 0, cmd);

            if (reply != null)
            {
                return reply.Payload as Commands.Debugging_Resolve_AppDomain.Reply;
            }

            return null;
        }

        public async Task<string> GetTypeNameAsync(uint td)
        {
            Commands.Debugging_Resolve_Type.Result resolvedType = await ResolveTypeAsync(td);

            return (resolvedType != null) ? resolvedType.m_name : null;
        }

        public async Task<string> GetMethodNameAsync(uint md, bool fIncludeType)
        {
            Commands.Debugging_Resolve_Method.Result resolvedMethod = await ResolveMethodAsync(md);
            string name = null;

            if (resolvedMethod != null)
            {
                if (fIncludeType)
                {
                    name = string.Format("{0}::{1}", await GetTypeNameAsync(resolvedMethod.m_td), resolvedMethod.m_name);
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
        public async Task<(string td, uint offset, uint success)> GetFieldNameAsync(uint fd)
        {
            Commands.Debugging_Resolve_Field.Result resolvedField = await ResolveFieldAsync(fd);

            if (resolvedField != null)
            {
                return (resolvedField.m_name, resolvedField.m_td, resolvedField.m_offset);
            }

            return (null, 0, 0);
        }

        public async Task<uint> GetVirtualMethodAsync(uint md, RuntimeValue obj)
        {
            Commands.Debugging_Resolve_VirtualMethod cmd = new Commands.Debugging_Resolve_VirtualMethod();

            cmd.m_md = md;
            cmd.m_obj = obj.ReferenceId;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Resolve_VirtualMethod, 0, cmd);

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
        public async Task<(ushort widthInWords, ushort heightInPixels, uint[] buf, bool success)> GetFrameBufferAsync()
        {
            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Lcd_GetFrame, 0, null);
            if (reply != null)
            {
                Commands.Debugging_Lcd_GetFrame.Reply cmdReply = reply.Payload as Commands.Debugging_Lcd_GetFrame.Reply;

                if (cmdReply != null)
                {
                    return (cmdReply.m_header.m_widthInWords, cmdReply.m_header.m_heightInPixels, cmdReply.m_data, true);
                }
            }

            return (0, 0, null, false);
        }

        //private void Adjust1bppOrientation(uint[] buf)
        //{
        //    //CLR_GFX_Bitmap::AdjustBitOrientation
        //    //The nanoCLR treats 1bpp bitmaps reversed from Windows
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

            await PerformRequestAsync(Commands.c_Debugging_Button_Inject, 0, cmd);
        }

        public async Task<List<ThreadStatus>> GetThreadsAsync()
        {
            List<ThreadStatus> threads = new List<ThreadStatus>();
            uint[] pids = await GetThreadListAsync();

            if (pids != null)
            {
                for (int i = 0; i < pids.Length; i++)
                {
                    Commands.Debugging_Thread_Stack.Reply reply = await GetThreadStackAsync(pids[i]);

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
                            ts.m_calls[depth - 1 - j] = String.Format("{0} [IP:{1:X4}]", await GetMethodNameAsync(reply.m_data[j].m_md, true), reply.m_data[j].m_IP);
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
        public async Task<(uint entrypoint, uint storageStart, uint storageLength, bool success)> DeploymentGetStatusWithResultAsync()
        {
            Commands.DebuggingDeploymentStatus.Reply status = await DeploymentGetStatusAsync();

            if (status != null)
            {
                return (status.EntryPoint, status.StorageStart, status.StorageLength, true);
            }
            else
            {
                return (0, 0, 0, false);
            }
        }

        public async Task<Commands.DebuggingDeploymentStatus.Reply> DeploymentGetStatusAsync()
        {
            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            Commands.DebuggingDeploymentStatus cmd = new Commands.DebuggingDeploymentStatus();
            Commands.DebuggingDeploymentStatus.Reply cmdReply = null;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Debugging_Deployment_Status, Flags.c_NoCaching, cmd, 2, 10000);

            if (reply != null)
            {
                cmdReply = reply.Payload as Commands.DebuggingDeploymentStatus.Reply;
            }

            return cmdReply;
        }

        public async Task<bool> Info_SetJMCAsyn(bool fJMC, ReflectionDefinition.Kind kind, uint index)
        {
            Commands.Debugging_Info_SetJMC cmd = new Commands.Debugging_Info_SetJMC();

            cmd.m_fIsJMC = (uint)(fJMC ? 1 : 0);
            cmd.m_kind = (uint)kind;
            cmd.m_raw = index;

            return IncomingMessage.IsPositiveAcknowledge(await PerformRequestAsync(Commands.c_Debugging_Info_SetJMC, 0, cmd));
        }

        private async Task<bool> DeploymentExecuteIncrementalAsync(List<byte[]> assemblies, IProgress<string> progress)
        {
            // get flash sector map from device
            var flashSectorMap = await GetFlashSectorMapAsync();

            // check if we do have the map
            if (flashSectorMap != null)
            {
                // total size of assemblies to deploy 
                int deployLength = assemblies.Sum(a => a.Length);

                // build the deployment blob from the flash sector map
                // apply a filter so that we take only the blocks flag for deployment 
                var deploymentBlob = flashSectorMap.Where(s => ((s.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_DEPLOYMENT)).Select(s => s.ToDeploymentSector()).ToList();

                while (assemblies.Count > 0)
                {
                    //
                    // Only word-aligned assemblies are allowed.
                    //
                    if (assemblies.First().Length % 4 != 0)
                    {
                        progress?.Report($"It's only possible to deploy word aligned assemblies. Failed to deploy assembly with {assemblies.First().Length} bytes.");

                        return false;
                    }

                    // setup counters
                    int remainingBytes = assemblies.First().Length;
                    int currentPosition = 0;

                    // find first block with available space
                    while (remainingBytes > 0)
                    {
                        // find the next sector with available space
                        var sector = deploymentBlob.First(s => s.AvailableSpace > 0);

                        int positionInSector = sector.Size - sector.AvailableSpace;
                        int bytesToCopy = Math.Min(sector.AvailableSpace, remainingBytes);

                        byte[] tempBuffer = new byte[bytesToCopy];

                        Array.Copy(assemblies.First(), tempBuffer, bytesToCopy);
                        sector.DeploymentData = tempBuffer;

                        remainingBytes -= bytesToCopy;
                        currentPosition += bytesToCopy;
                    }

                    if (remainingBytes == 0)
                    {
                        // asembly fully stored for deployment, remove it from the list
                        assemblies.RemoveAt(0);
                    }
                    else
                    {
                        // couldn't find enough space to deploy assembly!!
                        progress?.Report($"Deployment storage (total size: {deploymentBlob.ToDeploymentBlockList().Sum(b => b.Size)} bytes) was not large enough to fit assemblies to deploy (total size: {deployLength} bytes)");

                        return false;
                    }
                }

                // get the block list to deploy (not empty)
                var blocksToDeploy = deploymentBlob.ToDeploymentBlockList().FindAll(b => b.DeploymentData.Length > 0);

                foreach (DeploymentBlock block in blocksToDeploy)
                {
                    var eraseResult = await EraseMemoryAsync((uint)block.StartAddress, 1);
                    if (!eraseResult.success)
                    {
                        progress?.Report(($"FAILED to erase device memory @0x{block.StartAddress.ToString("X8")} with Length=0x{block.Size.ToString("X8")}"));

                        return false;
                    }

                    var writeResult = await WriteMemoryAsync((uint)block.StartAddress, block.DeploymentData);
                    if (!writeResult.success)
                    {
                        progress?.Report(($"FAILED to write device memory @0x{block.StartAddress.ToString("X8")} with Length={block.Size.ToString("X8")}"));

                        return false;
                    }

                    // report progress
                    // progress?.Report($"Deployed assemblies for a total size of {blocksToDeploy.Sum(b => b.Size)} bytes");
                }

                // deployment successfull
                return true;
            }

            // invalid flahs map
            // TODO provide feedback to user
            return false;
        }

        private async Task<bool> DeploymentExecuteFullAsync(List<byte[]> assemblies, IProgress<string> progress)
        {
            uint storageStart;
            uint deployLength;
            byte[] closeHeader = new byte[8];

            // perform request
            var reply = await DeploymentGetStatusWithResultAsync();

            // check if request was successfully executed
            if (!reply.success)
            {
                return false;
            }

            // fill in the local properties with the result
            storageStart = reply.storageStart;

            if (reply.storageLength == 0)
            {
                return false;
            }

            deployLength = (uint)closeHeader.Length;

            foreach (byte[] assembly in assemblies)
            {
                deployLength += (uint)assembly.Length;
            }

            progress?.Report(string.Format("Deploying assemblies for a total size of {0} bytes", deployLength));

            if (deployLength > reply.storageLength)
            {
                return false;
            }

            var eraseResult = await EraseMemoryAsync(storageStart, deployLength);

            if (!eraseResult.success)
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

                var writeResult1 = await WriteMemoryAsync(storageStart, assembly);

                if (!writeResult1.success)
                {
                    return false;
                }

                storageStart += (uint)assembly.Length;
            }

            var writeResult2 = await WriteMemoryAsync(storageStart, closeHeader);
            if (!writeResult2.success)
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
                throw new NotSupportedException("Current version only supports incremental deployment. Check the image source code for Debugging_Execution_QueryCLRCapabilities. The capabilities list has to include c_CapabilityFlags_IncrementalDeployment.");
                //progress?.Report("Deploying assemblies to device");

                //fDeployedOK = await DeploymentExecuteFullAsync(assemblies, progress);
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

        public async Task<(uint current, bool success)> SetProfilingModeAsync(uint iSet, uint iReset)
        {
            Commands.Profiling_Command cmd = new Commands.Profiling_Command();
            cmd.m_command = Commands.Profiling_Command.c_Command_ChangeConditions;
            cmd.m_parm1 = iSet;
            cmd.m_parm2 = iReset;

            IncomingMessage reply = await PerformRequestAsync(Commands.c_Profiling_Command, 0, cmd);
            if (reply != null)
            {
                Commands.Profiling_Command.Reply cmdReply = reply.Payload as Commands.Profiling_Command.Reply;

                if (cmdReply != null)
                {
                    return (cmdReply.m_raw, true);
                }
                else
                {
                    return (0, true);
                }
            }

            return (0, false);
        }

        public async Task<bool> FlushProfilingStreamAsync()
        {
            Commands.Profiling_Command cmd = new Commands.Profiling_Command();
            cmd.m_command = Commands.Profiling_Command.c_Command_FlushStream;
            await PerformRequestAsync(Commands.c_Profiling_Command, 0, cmd);
            return true;
        }

        private async Task<IncomingMessage> DiscoverCLRCapabilityAsync(uint caps)
        {
            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            Commands.Debugging_Execution_QueryCLRCapabilities cmd = new Commands.Debugging_Execution_QueryCLRCapabilities();

            cmd.m_caps = caps;

            return await PerformRequestAsync(Commands.c_Debugging_Execution_QueryCLRCapabilities, 0, cmd, 5, 1000);
        }

        private async Task<uint> DiscoverCLRCapabilityUintAsync(uint caps)
        {
            uint ret = 0;

            IncomingMessage reply = await DiscoverCLRCapabilityAsync(caps);

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

            return (CLRCapabilities.Capability)await DiscoverCLRCapabilityUintAsync(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityFlags);
        }

        private async Task<CLRCapabilities.SoftwareVersionProperties> DiscoverSoftwareVersionPropertiesAsync()
        {
            Debug.WriteLine("DiscoverSoftwareVersionProperties");

            IncomingMessage reply = await DiscoverCLRCapabilityAsync(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilitySoftwareVersion);

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

            IncomingMessage reply = await DiscoverCLRCapabilityAsync(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityLCD);

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

            IncomingMessage reply = await DiscoverCLRCapabilityAsync(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityHalSystemInfo);

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

            IncomingMessage reply = await DiscoverCLRCapabilityAsync(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityClrInfo);

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

        private async Task<CLRCapabilities.TargetInfoProperties> DiscoverTargetInfoPropertiesAsync()
        {
            Debug.WriteLine("==============================");
            Debug.WriteLine("DiscoverTargetInfoProperties");

            IncomingMessage reply = await DiscoverCLRCapabilityAsync(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilitySolutionReleaseInfo);

            ReleaseInfo targetInfo = new ReleaseInfo();

            CLRCapabilities.TargetInfoProperties targetInfoProps = new CLRCapabilities.TargetInfoProperties();

            if (reply != null)
            {
                Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_QueryCLRCapabilities.Reply;

                if (cmdReply != null && cmdReply.m_data != null)
                {
                    new Converter().Deserialize(targetInfo, cmdReply.m_data);

                    targetInfoProps = new CLRCapabilities.TargetInfoProperties(targetInfo.Version, targetInfo.Info);
                }
            }

            return targetInfoProps;
        }

        private async Task<CLRCapabilities> DiscoverCLRCapabilitiesAsync(CancellationToken cancellationToken)
        {
            var clrFlags = await DiscoverCLRCapabilityFlagsAsync();
            // check for cancelation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancelation requested");
                return null;
            }

            var clrLcd = await DiscoverCLRCapabilityLCDAsync();
            // check for cancelation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancelation requested");
                return null;
            }

            var softwareVersion = await DiscoverSoftwareVersionPropertiesAsync();
            // check for cancelation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancelation requested");
                return null;
            }

            var halSysInfo = await DiscoverHalSystemInfoPropertiesAsync();
            // check for cancelation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancelation requested");
                return null;
            }

            var clrInfo = await DiscoverClrInfoPropertiesAsync();
            // check for cancelation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancelation requested");
                return null;
            }

            var solutionInfo = await DiscoverTargetInfoPropertiesAsync();
            // check for cancelation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancelation requested");
                return null;
            }

            return new CLRCapabilities(clrFlags, clrLcd, softwareVersion, halSysInfo, clrInfo, solutionInfo);
        }

        #endregion

    }
}
