//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger
{
    public delegate void NoiseEventHandler(byte[] buffer, int offset, int count);
    public delegate void MessageEventHandler(IncomingMessage message, string text);
    public delegate void CommandEventHandler(IncomingMessage message, bool reply);

    public partial class Engine : IDisposable, IControllerHostLocal
    {
        private const int TIMEOUT_DEFAULT = 5000;

        internal IPort _portDefinition;
        internal Controller _controlller { get; set; }
        bool m_silent;

        DateTime m_lastNoise = DateTime.Now;

        public event EventHandler<StringEventArgs> SpuriousCharactersReceived;

        event NoiseEventHandler _eventNoise;
        event MessageEventHandler _eventMessage;
        event CommandEventHandler _eventCommand;
        event EventHandler _eventProcessExit;

        /// <summary>
        /// Notification thread is essentially the Tx thread. Other threads pump outgoing data into it, which after potential
        /// processing is sent out to destination synchronously.
        /// </summary>
        //Thread m_notificationThread;
        AutoResetEvent m_notifyEvent;
        //ArrayList m_notifyQueue;
        FifoBuffer m_notifyNoise;

        CancellationTokenSource _noiseHandlingCancellation = new CancellationTokenSource();

        CancellationTokenSource _backgroundProcessorCancellation = new CancellationTokenSource();

        AutoResetEvent _rpcEvent;
        //ArrayList m_rpcQueue;
        ArrayList _rpcEndPoints;

        ManualResetEvent m_evtShutdown;
        ManualResetEvent m_evtPing;
        TypeSysLookup m_typeSysLookup;
        EngineState _state;

        private Task _backgroundProcessor;

        protected readonly WireProtocolRequestsStore _requestsStore = new WireProtocolRequestsStore();
        protected readonly Timer _pendingRequestsTimer;


        private CancellationTokenSource _cancellationTokenSource = new CancellationTokenSource();
        public CancellationToken CancellationToken => _cancellationTokenSource.Token;

        internal INanoDevice Device;

        internal Engine(NanoDeviceBase device)
        {
            InitializeLocal(device);

            // default to false
            IsCRC32EnabledForWireProtocol = false;

            _pendingRequestsTimer = new Timer(ClearPendingRequests, null, 1000, 1000);
        }

        private void Initialize()
        {
            m_notifyEvent = new AutoResetEvent(false);
            _rpcEvent = new AutoResetEvent(false);
            m_evtShutdown = new ManualResetEvent(false);
            m_evtPing = new ManualResetEvent(false);

            //m_rpcQueue = ArrayList.Synchronized(new ArrayList());
            //m_rpcEndPoints = ArrayList.Synchronized(new ArrayList());
            //m_notifyQueue = ArrayList.Synchronized(new ArrayList());

            m_notifyNoise = new FifoBuffer();
            m_typeSysLookup = new TypeSysLookup();
            _state = new EngineState(this);

            //default capabilities, used until clr can be queried.
            Capabilities = new CLRCapabilities();

            // clear memory map
            FlashSectorMap = new List<Commands.Monitor_FlashSectorMap.FlashSectorData>();
        }

        private void InitializeLocal(NanoDeviceBase device)
        {
            _portDefinition = device.Parent;
            _controlller = new Controller(this);

            Device = (INanoDevice)device;

            Initialize();
        }

        public CLRCapabilities Capabilities { get; internal set; }

        public List<Commands.Monitor_FlashSectorMap.FlashSectorData> FlashSectorMap { get; private set; }

        public BinaryFormatter CreateBinaryFormatter()
        {
            return new BinaryFormatter(Capabilities);
        }

        public bool IsConnected { get; internal set; }

        public ConnectionSource ConnectionSource { get; internal set; }

        public bool IsConnectedTonanoCLR { get { return ConnectionSource == ConnectionSource.nanoCLR; } }

        public bool IsTargetBigEndian { get; internal set; }

        /// <summary>
        /// This flag is true when connected nanoDevice implements CRC32 in Wire Protocol packets and headers
        /// </summary>
        public bool IsCRC32EnabledForWireProtocol { get; internal set; }

        public bool StopDebuggerOnConnect { get; set; }

        public async Task<bool> ConnectAsync(int timeout, bool force = false, ConnectionSource connectionSource = ConnectionSource.Unknown)
        {
            if (force || IsConnected == false)
            {
                // connect to device 
                if (await Device.ConnectAsync())
                {
                    if (!IsRunning)
                    {
                        if(_state.GetValue() == EngineState.Value.NotStarted)
                        {
                            // background processor was never started
                            _state.SetValue(EngineState.Value.Starting, true);

                            // start task to process background messages
                            _backgroundProcessor = Task.Factory.StartNew(() => IncomingMessagesListenerAsync(), _backgroundProcessorCancellation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current);

                            _state.SetValue(EngineState.Value.Started, false);
                        }
                        else
                        {
                            // background processor is not running, start it
                            ResumeProcessing();
                        }
                    }

                    Commands.Monitor_Ping cmd = new Commands.Monitor_Ping();

                    cmd.m_source = Commands.Monitor_Ping.c_Ping_Source_Host;
                    cmd.m_dbg_flags = (StopDebuggerOnConnect ? Commands.Monitor_Ping.c_Ping_DbgFlag_Stop : 0);

                    IncomingMessage msg = PerformSyncRequest(Commands.c_Monitor_Ping, Flags.c_NoCaching, cmd);

                    if (msg == null || msg?.Payload == null)
                    {
                        // update flag
                        IsConnected = false;

                        // done here
                        return false;
                    }

                    Commands.Monitor_Ping.Reply reply = msg.Payload as Commands.Monitor_Ping.Reply;

                    if (reply != null)
                    {
                        IsTargetBigEndian = (reply.m_dbg_flags & Commands.Monitor_Ping.c_Ping_DbgFlag_BigEndian).Equals(Commands.Monitor_Ping.c_Ping_DbgFlag_BigEndian);

                        IsCRC32EnabledForWireProtocol = (reply.m_dbg_flags & Commands.Monitor_Ping.c_Ping_WPFlag_SupportsCRC32).Equals(Commands.Monitor_Ping.c_Ping_WPFlag_SupportsCRC32);

                        // update flag
                        IsConnected = true;

                        ConnectionSource = (reply == null || reply.m_source == Commands.Monitor_Ping.c_Ping_Source_NanoCLR) ? ConnectionSource.nanoCLR : ConnectionSource.nanoBooter;

                        if (m_silent)
                        {
                            SetExecutionMode(Commands.DebuggingExecutionChangeConditions.State.DebuggerQuiet, 0);
                        }

                        // resume execution for older clients, since server tools no longer do this.
                        if (!StopDebuggerOnConnect && (msg != null && msg.Payload == null))
                        {
                            ResumeExecution();
                        }
                    }
                }
            }

            if ((force || Capabilities.IsUnknown) && ConnectionSource == ConnectionSource.nanoCLR)
            {
                CancellationTokenSource cancellationTSource = new CancellationTokenSource();

                //default capabilities
                Capabilities = new CLRCapabilities();

                // clear memory map
                FlashSectorMap = new List<Commands.Monitor_FlashSectorMap.FlashSectorData>();

                var tempCapabilities = DiscoverCLRCapabilities(cancellationTSource.Token);

                if (tempCapabilities != null && !tempCapabilities.IsUnknown)
                {
                    Capabilities = tempCapabilities;
                    _controlller.Capabilities = Capabilities;
                }
                else
                {
                    // update flag
                    IsConnected = false;

                    // done here
                    return false;
                }
            }

            if (connectionSource != ConnectionSource.Unknown && connectionSource != ConnectionSource)
            {
                // update flag
                IsConnected = false;

                // done here
                return false;
            }

            return true;
        }

        private void ClearPendingRequests(object state)
        {
            var requestsToCancel = _requestsStore.FindAllToCancel();

            foreach (var wireProtocolRequest in requestsToCancel)
            {
                // cancel the task using the cancellation token if available or not requested
                var requestCanceled = wireProtocolRequest.CancellationToken.IsCancellationRequested
                    ? wireProtocolRequest.TaskCompletionSource.TrySetCanceled(wireProtocolRequest.CancellationToken)
                    : wireProtocolRequest.TaskCompletionSource.TrySetCanceled();

                // remove the request from the store
                if (_requestsStore.Remove(wireProtocolRequest.OutgoingMessage.Header) && requestCanceled)
                {
                    // TODO 
                    // invoke the event hook
                }
            }
        }

        public async Task IncomingMessagesListenerAsync()
        {
            var reassembler = new MessageReassembler(_controlller);

            while (!_backgroundProcessorCancellation.IsCancellationRequested && _state.IsRunning)
            {
                try
                {
                    await reassembler.ProcessAsync(_backgroundProcessorCancellation.Token);
                }
                catch (DeviceNotConnectedException)
                {
                    ProcessExited();
                    break;
                }
                catch (Exception ex)
                {
                    // look for I/O exception
                    // 0x800703E3
                    if (ex.HResult == -2147023901)
                    {
                        ProcessExited();
                        break;
                    }
                }
            }

            _state.SetValue(EngineState.Value.Stopping, false);

            Debug.WriteLine("**** EXIT IncomingMessagesListenerAsync ****");
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

        public bool ThrowOnCommunicationFailure { get; set; }

        #region Events 

        public event NoiseEventHandler OnNoise
        {
            add
            {
                _eventNoise += value;
            }

            remove
            {
                _eventNoise -= value;
            }
        }

        public event MessageEventHandler OnMessage
        {
            add
            {
                _eventMessage += value;
            }

            remove
            {
                _eventMessage -= value;
            }
        }

        public event CommandEventHandler OnCommand
        {
            add
            {
                _eventCommand += value;
            }

            remove
            {
                _eventCommand -= value;
            }
        }

        public event EventHandler OnProcessExit
        {
            add
            {
                _eventProcessExit += value;
            }

            remove
            {
                _eventProcessExit -= value;
            }
        }

        #endregion

        #region IDisposable Support

        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects).

                    //noiseHandlingCancellation.Cancel();

                    //backgroundProcessorCancellation.Cancel();

                    try
                    {
                        _cancellationTokenSource.Cancel();
                        _cancellationTokenSource.Dispose();
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

        private IncomingMessage PerformSyncRequest(uint command, uint flags, object payload, int millisecondsTimeout = 5000)
        {
            OutgoingMessage message = new OutgoingMessage(_controlller.GetNextSequenceId(), CreateConverter(), command, flags, payload);

            var request = PerformRequestAsync(message, _cancellationTokenSource.Token, millisecondsTimeout);

            try
            {
                Task.WaitAll(request);
            }
            catch(AggregateException)
            {
                return null;
            }

            return request.Result;
        }

        private IncomingMessage PerformSyncRequest(OutgoingMessage message, int millisecondsTimeout = 5000)
        {
            var request = PerformRequestAsync(message, _cancellationTokenSource.Token, millisecondsTimeout);

            try
            {
                Task.WaitAll(request);
            }
            catch (AggregateException)
            {
                return null;
            }

            return request.Result;
        }

        public Task<IncomingMessage> PerformRequestAsync(OutgoingMessage message, CancellationToken cancellationToken, int millisecondsTimeout = 5000)
        {
            WireProtocolRequest request = new WireProtocolRequest(message, cancellationToken, millisecondsTimeout);
            _requestsStore.Add(request);

            try
            {
                // Start a background task that will complete tcs1.Task
                Task.Factory.StartNew(() =>
                {
                    request.PerformRequestAsync(_controlller).Wait();
                });
            }
            catch (Exception ex)
            {
                // perform request failed, remove it from store
                _requestsStore.Remove(request.OutgoingMessage.Header);

                request.TaskCompletionSource.SetException(ex);

                return null;
            }

            return request.TaskCompletionSource.Task;
        }

        private List<IncomingMessage> PerformRequestBatch(List<OutgoingMessage> messages, int timeout = 1000)
        {
            List<IncomingMessage> replies = new List<IncomingMessage>();
            List<WireProtocolRequest> requests = new List<WireProtocolRequest>();

            foreach (OutgoingMessage message in messages)
            {
                replies.Add(PerformSyncRequest(message));
            }

            return replies;
        }

        public Commands.Monitor_Ping.Reply GetConnectionSource()
        {
            IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_Ping, 0, null);

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
            return await _portDefinition.SendBufferAsync(buffer, waiTimeout, cancellationToken);
        }

        public bool ProcessMessage(IncomingMessage message, bool isReply)
        {
            message.Payload = Commands.ResolveCommandToPayload(message.Header.Cmd, isReply, _controlller.Capabilities);


            if (isReply == true)
            {
                // we are processing a message flagged as a reply
                // OR we this is a reply to an explicit request

                WireProtocolRequest reply = _requestsStore.GetByReplyHeader(message.Header);

                if (reply != null)
                {
                    // remove from store
                    _requestsStore.Remove(reply.OutgoingMessage.Header);

                    // resolve the response
                    reply.TaskCompletionSource.TrySetResult(message);

                    return true;
                }
                else
                {
                    reply.TaskCompletionSource.TrySetResult(null);
                }
            }
            else
            {
                Packet bp = message.Header;

                switch (bp.Cmd)
                {
                    case Commands.c_Monitor_Ping:
                        {
                            Commands.Monitor_Ping.Reply cmdReply = new Commands.Monitor_Ping.Reply();

                            cmdReply.m_source = Commands.Monitor_Ping.c_Ping_Source_Host;
                            cmdReply.m_dbg_flags = (StopDebuggerOnConnect ? Commands.Monitor_Ping.c_Ping_DbgFlag_Stop : 0);

                            PerformRequestAsync(new OutgoingMessage(message, _controlller.CreateConverter(), Flags.c_NonCritical, cmdReply), _backgroundProcessorCancellation.Token).ConfigureAwait(false);

                            return true;
                        }

                    case Commands.c_Monitor_Message:
                        {
                            Commands.Monitor_Message payload = message.Payload as Commands.Monitor_Message;

                            Debug.Assert(payload != null);

                            if (payload != null)
                            {
                                _eventMessage?.Invoke(message, payload.ToString());
                            }

                            return true;
                        }

                    case Commands.c_Debugging_Messaging_Query:
                        Debug.Assert(message.Payload != null);
                        Task.Factory.StartNew(() => RpcReceiveQuery(message, (Commands.Debugging_Messaging_Query)message.Payload), _backgroundProcessorCancellation.Token);
                        break;

                    case Commands.c_Debugging_Messaging_Reply:
                        Debug.Assert(message.Payload != null);
                        Task.Factory.StartNew(() => RpcReceiveReplyAsync(message, (Commands.Debugging_Messaging_Reply)message.Payload), _backgroundProcessorCancellation.Token);
                        break;

                    case Commands.c_Debugging_Messaging_Send:
                        Debug.Assert(message.Payload != null);
                        Task.Factory.StartNew(() => RpcReceiveSendAsync(message, (Commands.Debugging_Messaging_Send)message.Payload), _backgroundProcessorCancellation.Token);
                        break;
                }
            }

            if (_eventCommand != null)
            {
                Task.Factory.StartNew(() => _eventCommand.Invoke(message, isReply), _backgroundProcessorCancellation.Token);

                return true;
            }

            return false;
        }

        public void SpuriousCharacters(byte[] buf, int offset, int count)
        {
            m_lastNoise = DateTime.Now;

            m_notifyNoise.Write(buf, offset, count);
        }

        public void ProcessExited()
        {
            Stop();

            _eventProcessExit?.Invoke(this, null);
        }

        public async Task<byte[]> ReadBufferAsync(uint bytesToRead, TimeSpan waitTimeout, CancellationToken cancellationToken)
        {
            return await _portDefinition.ReadBufferAsync(bytesToRead, waitTimeout, cancellationToken);
        }

        private OutgoingMessage CreateMessage(uint cmd, uint flags, object payload)
        {
            return new OutgoingMessage(_controlller.GetNextSequenceId(), CreateConverter(), cmd, flags, payload);
        }

        public void StopProcessing()
        {
            _state.SetValue(EngineState.Value.Stopping, false);

            m_evtShutdown.Set();

            if (_backgroundProcessor != null)
            {
                _backgroundProcessorCancellation.Cancel();
                _cancellationTokenSource.Cancel();
            }
        }

        public void ResumeProcessing()
        {
            m_evtShutdown.Reset();

            _state.SetValue(EngineState.Value.Resume, false);

            if ((_backgroundProcessor != null && _backgroundProcessor.IsCompleted) ||
                (_backgroundProcessor == null))
            {
                    _backgroundProcessor = Task.Factory.StartNew(() => IncomingMessagesListenerAsync(), _backgroundProcessorCancellation.Token, TaskCreationOptions.LongRunning, TaskScheduler.Current);
            }
        }

        public void Stop()
        {
            if (m_evtShutdown != null)
            {
                m_evtShutdown.Set();
            }

            if (_state.SetValue(EngineState.Value.Stopping, false))
            {
                StopProcessing();

                //((IController)this).ClosePort();

                _state.SetValue(EngineState.Value.Stopped, false);
            }
        }

        public bool IsRunning => _state.IsRunning;


        #region RPC Support

        // comment from original code REVIEW: Can this be refactored out of here to a separate class dedicated to RPC?
        internal class EndPointRegistration
        {
            internal class Request
            {
                public readonly EndPointRegistration Owner;

                public Request(EndPointRegistration owner)
                {
                    Owner = owner;
                }
            }

            internal class OutboundRequest : Request
            {
                private byte[] _data;
                private readonly AutoResetEvent _wait;
                public readonly uint Sequence;
                public readonly uint Type;
                public readonly uint Id;

                public OutboundRequest(EndPointRegistration owner, uint sequence, uint type, uint id)
                    : base(owner)
                {
                    Sequence = sequence;
                    Type = type;
                    Id = id;
                    _wait = new AutoResetEvent(false);
                }

                public byte[] Reply
                {
                    get { return _data; }

                    set
                    {
                        _data = value;
                        _wait.Set();
                    }
                }
                public WaitHandle WaitHandle
                {
                    get { return _wait; }
                }
            }

            internal class InboundRequest : Request
            {
                public readonly Message m_msg;

                public InboundRequest(EndPointRegistration owner, Message msg)
                    : base(owner)
                {
                    m_msg = msg;
                }
            }

            internal EndPoint m_ep;
            internal ArrayList m_req_Outbound;

            internal EndPointRegistration(EndPoint ep)
            {
                m_ep = ep;
                m_req_Outbound = ArrayList.Synchronized(new ArrayList());
            }

            internal void Destroy()
            {
                lock (m_req_Outbound.SyncRoot)
                {
                    foreach (OutboundRequest or in m_req_Outbound)
                    {
                        or.Reply = null;
                    }
                }

                m_req_Outbound.Clear();
            }
        }

        internal void RpcRegisterEndPoint(EndPoint ep)
        {
            EndPointRegistration eep = RpcFind(ep);
            bool fSuccess = false;

            if (eep == null)
            {
                IControllerRemote remote = _controlller as IControllerRemote;

                if (remote != null)
                {
                    fSuccess = remote.RegisterEndpoint(ep._type, ep._id);
                }
                else
                {
                    fSuccess = true;
                }

                if (fSuccess)
                {
                    lock (_rpcEndPoints.SyncRoot)
                    {
                        eep = RpcFind(ep);

                        if (eep == null)
                        {
                            _rpcEndPoints.Add(new EndPointRegistration(ep));
                        }
                        else
                        {
                            fSuccess = false;
                        }
                    }
                }
            }

            if (!fSuccess)
            {
                throw new ApplicationException("Endpoint already registered.");
            }
        }

        internal void RpcDeregisterEndPoint(EndPoint ep)
        {
            EndPointRegistration eep = RpcFind(ep);

            if (eep != null)
            {
                _rpcEndPoints.Remove(eep);

                eep.Destroy();

                IControllerRemote remote = _controlller as IControllerRemote;
                if (remote != null)
                {
                    remote.DeregisterEndpoint(ep._type, ep._id);
                }
            }
        }

        private EndPointRegistration RpcFind(EndPoint ep)
        {
            return RpcFind(ep._type, ep._id, false);
        }

        private EndPointRegistration RpcFind(uint type, uint id, bool fOnlyServer)
        {
            lock (_rpcEndPoints.SyncRoot)
            {
                foreach (EndPointRegistration eep in _rpcEndPoints)
                {
                    EndPoint ep = eep.m_ep;

                    if (ep._type == type && ep._id == id)
                    {
                        if (!fOnlyServer || ep.IsRpcServer)
                        {
                            return eep;
                        }
                    }
                }
            }
            return null;
        }

        private async Task RpcReceiveQuery(IncomingMessage message, Commands.Debugging_Messaging_Query query)
        {
            Commands.Debugging_Messaging_Address addr = query.m_addr;
            EndPointRegistration eep = RpcFind(addr.m_to_Type, addr.m_to_Id, true);

            Commands.Debugging_Messaging_Query.Reply reply = new Commands.Debugging_Messaging_Query.Reply();

            reply.m_found = (eep != null) ? 1u : 0u;
            reply.m_addr = addr;

            await PerformRequestAsync(new OutgoingMessage(message, CreateConverter(), Flags.c_NonCritical, reply), _cancellationTokenSource.Token);
        }

        internal bool RpcCheck(Commands.Debugging_Messaging_Address addr)
        {
            Commands.Debugging_Messaging_Query cmd = new Commands.Debugging_Messaging_Query();

            cmd.m_addr = addr;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Messaging_Query, 0, cmd);
            if (reply != null)
            {
                Commands.Debugging_Messaging_Query.Reply res = reply.Payload as Commands.Debugging_Messaging_Query.Reply;

                if (res != null && res.m_found != 0)
                {
                    return true;
                }
            }

            return false;
        }

        internal byte[] RpcSend(Commands.Debugging_Messaging_Address addr, int timeout, byte[] data)
        {
            EndPointRegistration.OutboundRequest or = null;
            byte[] res = null;

            try
            {
                or = RpcSend_Setup(addr, data);
                if (or != null)
                {
                    or.WaitHandle.WaitOne(timeout);

                    res = or.Reply;
                }
            }
            finally
            {
                if (or != null)
                {
                    or.Owner.m_req_Outbound.Remove(or);
                }
            }

            return res;
        }

        private EndPointRegistration.OutboundRequest RpcSend_Setup(Commands.Debugging_Messaging_Address addr, byte[] data)
        {
            EndPointRegistration eep = RpcFind(addr.m_from_Type, addr.m_from_Id, false);
            EndPointRegistration.OutboundRequest or = null;

            if (eep != null)
            {
                bool fSuccess = false;

                or = new EndPointRegistration.OutboundRequest(eep, addr.m_seq, addr.m_to_Type, addr.m_to_Id);

                eep.m_req_Outbound.Add(or);

                Commands.Debugging_Messaging_Send cmd = new Commands.Debugging_Messaging_Send();

                cmd.m_addr = addr;
                cmd.m_data = data;

                IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Messaging_Send, 0, cmd);
                if (reply != null)
                {
                    Commands.Debugging_Messaging_Send.Reply res = reply.Payload as Commands.Debugging_Messaging_Send.Reply;

                    if (res != null && res.m_found != 0)
                    {
                        fSuccess = true;
                    }
                }

                // FIXME
                //if (!IsRunning)
                //{
                //    fSuccess = false;
                //}

                if (!fSuccess)
                {
                    eep.m_req_Outbound.Remove(or);

                    or = null;
                }
            }

            return or;
        }

        private async Task RpcReceiveSendAsync(IncomingMessage msg, Commands.Debugging_Messaging_Send send)
        {
            Commands.Debugging_Messaging_Address addr = send.m_addr;
            EndPointRegistration eep;

            eep = RpcFind(addr.m_to_Type, addr.m_to_Id, true);

            Commands.Debugging_Messaging_Send.Reply res = new Commands.Debugging_Messaging_Send.Reply();

            res.m_found = (eep != null) ? 1u : 0u;
            res.m_addr = addr;

            await PerformRequestAsync(new OutgoingMessage(msg, CreateConverter(), Flags.c_NonCritical, res), _cancellationTokenSource.Token);

            if (eep != null)
            {
                Message msgNew = new Message(eep.m_ep, addr, send.m_data);

                EndPointRegistration.InboundRequest ir = new EndPointRegistration.InboundRequest(eep, msgNew);

                // FIXME
                //ThreadPool.QueueUserWorkItem(new WaitCallback(RpcReceiveSendDispatch), ir);
            }
        }

        private void RpcReceiveSendDispatch(object obj)
        {
            EndPointRegistration.InboundRequest ir = (EndPointRegistration.InboundRequest)obj;

            if (IsRunning)
            {
                ir.Owner.m_ep.DispatchMessage(ir.m_msg);
            }
        }

        internal bool RpcReply(Commands.Debugging_Messaging_Address addr, byte[] data)
        {
            Commands.Debugging_Messaging_Reply cmd = new Commands.Debugging_Messaging_Reply();

            cmd.m_addr = addr;
            cmd.m_data = data;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Messaging_Reply, 0, cmd);
            if (reply != null)
            {
                Commands.Debugging_Messaging_Reply.Reply res = new Commands.Debugging_Messaging_Reply.Reply();

                if (res != null && res.m_found != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private async Task RpcReceiveReplyAsync(IncomingMessage message, Commands.Debugging_Messaging_Reply reply)
        {
            Commands.Debugging_Messaging_Address addr = reply.m_addr;
            EndPointRegistration eep;

            eep = RpcFind(addr.m_from_Type, addr.m_from_Id, false);

            Commands.Debugging_Messaging_Reply.Reply res = new Commands.Debugging_Messaging_Reply.Reply();

            res.m_found = (eep != null) ? 1u : 0u;
            res.m_addr = addr;

            await PerformRequestAsync(new OutgoingMessage(message, CreateConverter(), Flags.c_NonCritical, res), _cancellationTokenSource.Token);

            if (eep != null)
            {
                lock (eep.m_req_Outbound.SyncRoot)
                {
                    foreach (EndPointRegistration.OutboundRequest or in eep.m_req_Outbound)
                    {
                        if (or.Sequence == addr.m_seq && or.Type == addr.m_to_Type && or.Id == addr.m_to_Id)
                        {
                            or.Reply = reply.m_data;

                            break;
                        }
                    }
                }
            }
        }

        internal uint RpcGetUniqueEndpointId()
        {
            return _controlller.GetUniqueEndpointId();
        }

        #endregion


        internal async Task<WireProtocolRequest> RequestAsync(OutgoingMessage message, int timeout)
        {
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                //Checking whether IsRunning and adding the request to m_requests
                //needs to be atomic to avoid adding a request after the Engine
                //has been stopped.

                if (!IsRunning)
                {
                    throw new ApplicationException("Engine is not running or process has exited.");
                }

                WireProtocolRequest request = new WireProtocolRequest(message, cts.Token);
                _requestsStore.Add(request);

                try
                {
                    await request.PerformRequestAsync(_controlller);
                }
                catch
                {
                    _requestsStore.Remove(message.Header);
                }

                return request;
            }
        }

        /// <summary>
        /// Global lock object for synchronizing message request. This ensures there is only one
        /// outstanding request at any point of time. 
        /// </summary>
        internal object m_ReqSyncLock = new object();

        private Task<WireProtocolRequest> AsyncMessage(uint command, uint flags, object payload, int timeout)
        {
            OutgoingMessage msg = CreateMessage(command, flags, payload);

            return RequestAsync(msg, timeout);
        }

        private async Task<IncomingMessage> MessageAsync(uint command, uint flags, object payload, int timeout)
        {
            // FIXME
            // Lock on m_ReqSyncLock object, so only one thread is active inside the block.
            //lock (m_ReqSyncLock)
            //{
            WireProtocolRequest req = await AsyncMessage(command, flags, payload, timeout);

            //return await req.WaitAsync();
            return null;
            //}
        }

        private async Task<IncomingMessage> SyncMessageAsync(uint command, uint flags, object payload)
        {
            return await MessageAsync(command, flags, payload, TIMEOUT_DEFAULT);
        }


        #region Commands implementation

        public List<Commands.Monitor_MemoryMap.Range> GetMemoryMap()
        {
            Commands.Monitor_MemoryMap cmd = new Commands.Monitor_MemoryMap();

            IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_MemoryMap, 0, cmd);

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

        public List<Commands.Monitor_DeploymentMap.DeploymentData> GetDeploymentMap()
        {
            Commands.Monitor_DeploymentMap cmd = new Commands.Monitor_DeploymentMap();

            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_DeploymentMap, 0, cmd);

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

        public Commands.Monitor_OemInfo.Reply GetMonitorOemInfo()
        {
            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_OemInfo, 0, null);

            if (reply != null)
            {
                return reply.Payload as Commands.Monitor_OemInfo.Reply;
            }

            return null;
        }

        public List<Commands.Monitor_FlashSectorMap.FlashSectorData> GetFlashSectorMap()
        {
            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_FlashSectorMap, 0, null);

            if (reply != null)
            {
                var cmdReply = reply.Payload as Commands.Monitor_FlashSectorMap.Reply;

                if (cmdReply != null)
                {
                    // update property
                    FlashSectorMap = cmdReply.m_map;


                    return cmdReply.m_map;
                }
            }

            return null;
        }

        private (byte[] Buffer, bool Success) ReadMemory(uint address, uint length, uint offset)
        {
            byte[] buffer = new byte[length];

            while (length > 0)
            {
                Commands.Monitor_ReadMemory cmd = new Commands.Monitor_ReadMemory();

                cmd.m_address = address;
                cmd.m_length = length;

                IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_ReadMemory, 0, cmd);
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

            return (buffer, true);
        }

        public (byte[] Buffer, bool Success) ReadMemory(uint address, uint length)
        {
            return ReadMemory(address, length, 0);
        }

        public (uint ErrorCode, bool Success) WriteMemory(uint address, byte[] buf, int offset, int length)
        {
            int count = length;
            int position = offset;

            while (count > 0)
            {
                Commands.Monitor_WriteMemory cmd = new Commands.Monitor_WriteMemory();

                // get packet length, either the maximum allowed size or whatever is still available to TX
                int packetLength = Math.Min(1024, count);

                cmd.PrepareForSend(address, buf, position, packetLength);

                DebuggerEventSource.Log.EngineWriteMemory(address, packetLength);

                IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_WriteMemory, 0, cmd);

                if (reply != null)
                {
                    Commands.Monitor_WriteMemory.Reply cmdReply = reply.Payload as Commands.Monitor_WriteMemory.Reply;

                    if (!reply.IsPositiveAcknowledge() || cmdReply.ErrorCode != 0)
                    {
                        return (cmdReply.ErrorCode, false);
                    }

                    address += (uint)packetLength;
                    count -= packetLength;
                    position += packetLength;
                }
                else
                {
                    return (0, false);
                }
            }

            return (0, true);
        }

        public (uint ErrorCode, bool Success) WriteMemory(uint address, byte[] buf)
        {
            return WriteMemory(address, buf, 0, buf.Length);
        }

        public (uint ErrorCode, bool Success) EraseMemory(uint address, uint length)
        {
            DebuggerEventSource.Log.EngineEraseMemory(address, length);

            var cmd = new Commands.Monitor_EraseMemory
            {
                m_address = address,
                m_length = length
            };

            // typical max Flash erase times for STM32 parts with PSIZE set to 16bits are:
            // 16kB sector:  600ms  >> 38ms/kB
            const int eraseTimeout16kSector = 600;

            // 64kB sector:  1400ms >> 22ms/kB
            const int eraseTimeout64kSector = 1400;

            // 128kB sector: 2600ms >> 21ms/kB
            const int eraseTimeout128kSector = 2600;

            // this extra timeout is to account comm times and execution operation on the AccessMemory function
            const int extraTimeoutForErase = 800;

            // the erase memory command isn't aware of the sector(s) size it will end up erasing so we have to do an educated guess on how long that will take
            // considering the worst case timing which is the erase of the smallest sector.

            // default timeout is 0ms
            var timeout = 0;

            if (length <= (16 * 1024))
            {
                // timeout for 16kB sector
                timeout = eraseTimeout16kSector + extraTimeoutForErase;
            }
            else if (length <= (64 * 1024))
            {
                // timeout for 64kB sector
                timeout = eraseTimeout64kSector + extraTimeoutForErase;
            }
            else if (length <= (128 * 1024))
            {
                // timeout for 128kB sector
                timeout = eraseTimeout128kSector + extraTimeoutForErase;
            }
            else
            {
                // timeout for anything above 128kB (multiple sectors)
                timeout = (int)(length / (16 * 1024)) * eraseTimeout16kSector + 2 * extraTimeoutForErase;
            }

            IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_EraseMemory, 0, cmd);

            if (reply != null)
            {
                Commands.Monitor_EraseMemory.Reply cmdReply = reply.Payload as Commands.Monitor_EraseMemory.Reply;

                return (cmdReply?.ErrorCode ?? 0, reply.IsPositiveAcknowledge());
            }

            return (0, false);
        }

        public bool ExecuteMemory(uint address)
        {
            Commands.Monitor_Execute cmd = new Commands.Monitor_Execute();

            cmd.m_address = address;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_Execute, 0, cmd);

            if (reply != null)
            {
                return reply.IsPositiveAcknowledge();
            }

            return false;
        }

        public void RebootDevice(RebootOptions options = RebootOptions.NormalReboot)
        {
            Commands.MonitorReboot cmd = new Commands.MonitorReboot();

            bool fThrowOnCommunicationFailureSav = ThrowOnCommunicationFailure;

            ThrowOnCommunicationFailure = false;

            // check if device can handle soft reboot
            if(Capabilities.SoftReboot)
            {
                cmd.flags = (uint)options;
            }
            else
            {
                // device can't soft reboot, so a normal reboot will have to do it
                cmd.flags = (uint)RebootOptions.NormalReboot;
            }

            try
            {
                m_evtPing.Reset();

                IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_Reboot, Flags.c_NoCaching, cmd);
            }
            finally
            {
                ThrowOnCommunicationFailure = fThrowOnCommunicationFailureSav;
            }
        }

        public async Task<bool> ReconnectAsync(bool fSoftReboot, int timeout = 5000)
        {
            if (!await ConnectAsync(timeout, true, ConnectionSource.Unknown))
            {
                if (ThrowOnCommunicationFailure)
                {
                    throw new Exception("Could not reconnect to nanoCLR");
                }
                return false;
            }

            return true;
        }

        public uint GetExecutionBasePtr()
        {
            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_BasePtr, 0, null);
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

        public Commands.DebuggingExecutionChangeConditions.State GetExecutionMode()
        {
            Commands.DebuggingExecutionChangeConditions cmd = new Commands.DebuggingExecutionChangeConditions();

            // setting these to 0 won't change anything in the target when the command is executed
            // BUT, because the current state is returned we can parse the return result to get the state
            cmd.FlagsToSet = 0;
            cmd.FlagsToReset = 0;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_ChangeConditions, Flags.c_NoCaching, cmd);
            if (reply != null)
            {
                // need this to get DebuggingExecutionChangeConditions.State enum from raw value
                Commands.DebuggingExecutionChangeConditions.Reply cmdReply = reply.Payload as Commands.DebuggingExecutionChangeConditions.Reply;

                if (cmdReply != null)
                {
                    return (Commands.DebuggingExecutionChangeConditions.State)cmdReply.CurrentState;
                }
            }

            // default to unknown
            return Commands.DebuggingExecutionChangeConditions.State.Unknown;
        }

        public bool SetExecutionMode(Commands.DebuggingExecutionChangeConditions.State flagsToSet, Commands.DebuggingExecutionChangeConditions.State flagsToReset)
        {
            Commands.DebuggingExecutionChangeConditions cmd = new Commands.DebuggingExecutionChangeConditions();

            cmd.FlagsToSet = (uint)flagsToSet;
            cmd.FlagsToReset = (uint)flagsToReset;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_ChangeConditions, Flags.c_NoCaching, cmd);
            if (reply != null)
            {
                Commands.DebuggingExecutionChangeConditions.Reply cmdReply = reply.Payload as Commands.DebuggingExecutionChangeConditions.Reply;

                if (cmdReply != null)
                {
                    if (cmdReply.CurrentState != (uint)Commands.DebuggingExecutionChangeConditions.State.Unknown)
                        return true;
                }
            }

            // default to false 
            return false;
        }

        public bool PauseExecution()
        {
            return SetExecutionMode(Commands.DebuggingExecutionChangeConditions.State.Stopped, 0);
        }

        public bool ResumeExecution()
        {
            return SetExecutionMode(0, Commands.DebuggingExecutionChangeConditions.State.Stopped);
        }

        public bool SetCurrentAppDomain(uint id)
        {
            Commands.Debugging_Execution_SetCurrentAppDomain cmd = new Commands.Debugging_Execution_SetCurrentAppDomain();

            cmd.m_id = id;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_SetCurrentAppDomain, 0, cmd);

            if (reply != null)
            {
                return reply.IsPositiveAcknowledge();
            }

            return false;
        }

        public bool SetBreakpoints(Commands.Debugging_Execution_BreakpointDef[] breakpoints)
        {
            Commands.Debugging_Execution_Breakpoints cmd = new Commands.Debugging_Execution_Breakpoints();

            cmd.m_data = breakpoints;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_Breakpoints, 0, cmd);

            if (reply != null)
            {
                return reply.IsPositiveAcknowledge();
            }

            return false;
        }

        public Commands.Debugging_Execution_BreakpointDef GetBreakpointStatus()
        {
            Commands.Debugging_Execution_BreakpointStatus cmd = new Commands.Debugging_Execution_BreakpointStatus();

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_BreakpointStatus, 0, cmd);

            if (reply != null)
            {
                Commands.Debugging_Execution_BreakpointStatus.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_BreakpointStatus.Reply;

                if (cmdReply != null)
                    return cmdReply.m_lastHit;
            }

            return null;
        }

        public bool SetSecurityKey(byte[] key)
        {
            Commands.Debugging_Execution_SecurityKey cmd = new Commands.Debugging_Execution_SecurityKey();

            cmd.m_key = key;

            return PerformSyncRequest(Commands.c_Debugging_Execution_SecurityKey, 0, cmd) != null;
        }

        public bool UnlockDevice(byte[] blob)
        {
            Commands.Debugging_Execution_Unlock cmd = new Commands.Debugging_Execution_Unlock();

            Array.Copy(blob, 0, cmd.m_command, 0, 128);
            Array.Copy(blob, 128, cmd.m_hash, 0, 128);

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_Unlock, 0, cmd);

            if (reply != null)
            {
                return reply.IsPositiveAcknowledge();
            }

            return false;
        }

        public (uint Address, bool Success) AllocateMemory(uint size)
        {
            Commands.Debugging_Execution_Allocate cmd = new Commands.Debugging_Execution_Allocate();

            cmd.m_size = size;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_Allocate, 0, cmd);
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

        public bool CanUpgradeToSsl()
        {
            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            Commands.Debugging_UpgradeToSsl cmd = new Commands.Debugging_UpgradeToSsl();

            cmd.m_flags = 0;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_UpgradeToSsl, Flags.c_NoCaching, cmd);

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
        public int StartUpdate(
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

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_MFUpdate_Start, Flags.c_NoCaching, cmd);

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

        public (byte[] Response, bool Success) UpdateAuthCommand(int updateHandle, uint authCommand, byte[] commandArgs)
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

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_MFUpdate_AuthCmd, Flags.c_NoCaching, cmd);

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

        public bool UpdateAuthenticate(int updateHandle, byte[] authenticationData)
        {
            Commands.Debugging_MFUpdate_Authenticate cmd = new Commands.Debugging_MFUpdate_Authenticate();

            cmd.m_updateHandle = updateHandle;
            cmd.PrepareForSend(authenticationData);

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_MFUpdate_Authenticate, Flags.c_NoCaching, cmd);

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

        private bool UpdateGetMissingPackets(int updateHandle)
        {
            Commands.Debugging_MFUpdate_GetMissingPkts cmd = new Commands.Debugging_MFUpdate_GetMissingPkts();

            cmd.m_updateHandle = updateHandle;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_MFUpdate_GetMissingPkts, Flags.c_NoCaching, cmd);

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

        public bool AddPacket(int updateHandle, uint packetIndex, byte[] packetData, uint packetValidation)
        {
            if (!m_updateMissingPktTbl.ContainsKey(updateHandle))
            {
                UpdateGetMissingPackets(updateHandle);
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

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_MFUpdate_AddPacket, Flags.c_NoCaching, cmd);
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

        public bool InstallUpdate(int updateHandle, byte[] validationData)
        {
            if (m_updateMissingPktTbl.ContainsKey(updateHandle))
            {
                m_updateMissingPktTbl.Remove(updateHandle);
            }

            Commands.Debugging_MFUpdate_Install cmd = new Commands.Debugging_MFUpdate_Install();

            cmd.m_updateHandle = updateHandle;

            cmd.PrepareForSend(validationData);

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_MFUpdate_Install, Flags.c_NoCaching, cmd);

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

        public uint CreateThread(uint methodIndex, int scratchPadLocation)
        {
            return CreateThread(methodIndex, scratchPadLocation, 0);
        }

        public uint CreateThread(uint methodIndex, int scratchPadLocation, uint pid)
        {
            if (Capabilities.ThreadCreateEx)
            {
                Commands.Debugging_Thread_CreateEx cmd = new Commands.Debugging_Thread_CreateEx();

                cmd.m_md = methodIndex;
                cmd.m_scratchPad = scratchPadLocation;
                cmd.m_pid = pid;

                IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Thread_CreateEx, 0, cmd);

                if (reply != null)
                {
                    Commands.Debugging_Thread_CreateEx.Reply cmdReply = reply.Payload as Commands.Debugging_Thread_CreateEx.Reply;

                    return cmdReply.m_pid;
                }
            }

            return 0;
        }

        public uint[] GetThreadList()
        {
            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Thread_List, 0, null);

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

        public Commands.Debugging_Thread_Stack.Reply GetThreadStack(uint pid)
        {
            Commands.Debugging_Thread_Stack cmd = new Commands.Debugging_Thread_Stack
            {
                m_pid = pid
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Thread_Stack, 0, cmd);

            if (reply != null)
            {
                return reply.Payload as Commands.Debugging_Thread_Stack.Reply;
            }

            return null;
        }

        public bool KillThread(uint pid)
        {
            Commands.Debugging_Thread_Kill cmd = new Commands.Debugging_Thread_Kill();

            cmd.m_pid = pid;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Thread_Kill, 0, cmd);

            if (reply != null)
            {
                Commands.Debugging_Thread_Kill.Reply cmdReply = reply.Payload as Commands.Debugging_Thread_Kill.Reply;

                return cmdReply.m_result != 0;
            }

            return false;
        }

        public bool SuspendThread(uint pid)
        {
            Commands.Debugging_Thread_Suspend cmd = new Commands.Debugging_Thread_Suspend();

            cmd.m_pid = pid;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Thread_Suspend, 0, cmd);

            if (reply != null)
            {
                return reply.IsPositiveAcknowledge();
            }

            return false;
        }

        public bool ResumeThread(uint pid)
        {
            Commands.Debugging_Thread_Resume cmd = new Commands.Debugging_Thread_Resume();

            cmd.m_pid = pid;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Thread_Resume, 0, cmd);

            if (reply != null)
            {
                return reply.IsPositiveAcknowledge();
            }

            return false;
        }

        public RuntimeValue GetThreadException(uint pid)
        {
            Commands.Debugging_Thread_GetException cmd = new Commands.Debugging_Thread_GetException();

            cmd.m_pid = pid;

            return GetRuntimeValue(Commands.c_Debugging_Thread_GetException, cmd);
        }

        public RuntimeValue GetThread(uint pid)
        {
            Commands.Debugging_Thread_Get cmd = new Commands.Debugging_Thread_Get();

            cmd.m_pid = pid;

            return GetRuntimeValue(Commands.c_Debugging_Thread_Get, cmd);
        }

        public bool UnwindThread(uint pid, uint depth)
        {
            Commands.Debugging_Thread_Unwind cmd = new Commands.Debugging_Thread_Unwind();

            cmd.m_pid = pid;
            cmd.m_depth = depth;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Thread_Unwind, 0, cmd);

            if (reply != null)
            {
                return reply.IsPositiveAcknowledge();
            }

            return false;
        }

        public bool SetIPOfStackFrame(uint pid, uint depth, uint IP, uint depthOfEvalStack)
        {
            Commands.Debugging_Stack_SetIP cmd = new Commands.Debugging_Stack_SetIP();

            cmd.m_pid = pid;
            cmd.m_depth = depth;

            cmd.m_IP = IP;
            cmd.m_depthOfEvalStack = depthOfEvalStack;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Stack_SetIP, 0, cmd);

            if (reply != null)
            {
                return reply.IsPositiveAcknowledge();
            }

            return false;
        }

        public Commands.Debugging_Stack_Info.Reply GetStackInfo(uint pid, uint depth)
        {
            Commands.Debugging_Stack_Info cmd = new Commands.Debugging_Stack_Info();

            cmd.m_pid = pid;
            cmd.m_depth = depth;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Stack_Info, 0, cmd);

            if (reply != null)
            {
                return reply.Payload as Commands.Debugging_Stack_Info.Reply;
            }

            return null;
        }

        //--//

        public Commands.Debugging_TypeSys_AppDomains.Reply GetAppDomains()
        {
            if (!Capabilities.AppDomains)
                return null;

            Commands.Debugging_TypeSys_AppDomains cmd = new Commands.Debugging_TypeSys_AppDomains();

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_TypeSys_AppDomains, 0, cmd);

            if (reply != null)
            {
                return reply.Payload as Commands.Debugging_TypeSys_AppDomains.Reply;
            }

            return null;
        }

        public Commands.Debugging_TypeSys_Assemblies.Reply GetAssemblies()
        {
            Commands.Debugging_TypeSys_Assemblies cmd = new Commands.Debugging_TypeSys_Assemblies();

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_TypeSys_Assemblies, 0, cmd);

            if (reply != null)
            {
                return reply.Payload as Commands.Debugging_TypeSys_Assemblies.Reply;
            }

            return null;
        }

        public List<Commands.DebuggingResolveAssembly> ResolveAllAssemblies()
        {
            Commands.Debugging_TypeSys_Assemblies.Reply assemblies = GetAssemblies();
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

                List<IncomingMessage> replies = PerformRequestBatch(requests);

                foreach (IncomingMessage message in replies)
                {
                    if (message == null)
                    {
                        // can't happen, failing right now
                        break;
                    }

                    if(!message.IsPositiveAcknowledge())
                    {
                        // can't happen, failing right now
                        break;
                    }

                    // reply is a match for request which m_seq is same as reply m_seqReply
                    // need to check for null or invalid payload
                    var payload = requests.Find(req => req.Header.Seq == message.Header.SeqReply).Payload;

                    if (payload != null)
                    {
                        resolveAssemblies.Add(payload as Commands.DebuggingResolveAssembly);
                        resolveAssemblies[resolveAssemblies.Count - 1].Result = message.Payload as Commands.DebuggingResolveAssembly.Reply;
                    }
                    else
                    {
                        // failure
                        break;
                    }
                }
            }

            return resolveAssemblies;
        }

        public Commands.DebuggingResolveAssembly.Reply ResolveAssembly(uint idx)
        {
            Commands.DebuggingResolveAssembly cmd = new Commands.DebuggingResolveAssembly();

            cmd.Idx = idx;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Resolve_Assembly, 0, cmd);

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
        public (uint NumOfArguments, uint NnumOfLocals, uint DepthOfEvalStack, bool Success) GetStackFrameInfo(uint pid, uint depth)
        {
            Commands.Debugging_Stack_Info.Reply reply = GetStackInfo(pid, depth);

            if (reply == null)
            {
                return (0, 0, 0, false);
            }

            return (reply.m_numOfArguments, reply.m_numOfLocals, reply.m_depthOfEvalStack, true);
        }

        private RuntimeValue GetRuntimeValue(uint msg, object cmd)
        {
            IncomingMessage reply = PerformSyncRequest(msg, 0, cmd);

            if (reply != null && reply.Payload != null)
            {
                Commands.Debugging_Value_Reply cmdReply = reply.Payload as Commands.Debugging_Value_Reply;

                return RuntimeValue.Convert(this, cmdReply.m_values);
            }

            return null;
        }

        internal RuntimeValue GetFieldValue(RuntimeValue val, uint offset, uint fd)
        {
            Commands.Debugging_Value_GetField cmd = new Commands.Debugging_Value_GetField();

            cmd.m_heapblock = (val == null ? 0 : val.m_handle.m_referenceID);
            cmd.m_offset = offset;
            cmd.m_fd = fd;

            return GetRuntimeValue(Commands.c_Debugging_Value_GetField, cmd);
        }

        public RuntimeValue GetStaticFieldValue(uint fd)
        {
            return GetFieldValue(null, 0, fd);
        }

        internal RuntimeValue AssignRuntimeValue(uint heapblockSrc, uint heapblockDst)
        {
            Commands.Debugging_Value_Assign cmd = new Commands.Debugging_Value_Assign();

            cmd.m_heapblockSrc = heapblockSrc;
            cmd.m_heapblockDst = heapblockDst;

            return GetRuntimeValue(Commands.c_Debugging_Value_Assign, cmd);
        }

        internal bool SetBlock(uint heapblock, uint dt, byte[] data)
        {
            Commands.Debugging_Value_SetBlock setBlock = new Commands.Debugging_Value_SetBlock();

            setBlock.m_heapblock = heapblock;
            setBlock.m_dt = dt;

            data.CopyTo(setBlock.m_value, 0);

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Value_SetBlock, 0, setBlock);

            if (reply != null)
            {
                return reply.IsPositiveAcknowledge();
            }

            return false;
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

        public bool ResizeScratchPad(int size)
        {
            Commands.Debugging_Value_ResizeScratchPad cmd = new Commands.Debugging_Value_ResizeScratchPad();

            cmd.m_size = size;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Value_ResizeScratchPad, 0, cmd);

            if (reply != null)
            {
                return reply.IsPositiveAcknowledge();
            }

            return false;
        }

        public RuntimeValue GetStackFrameValue(uint pid, uint depth, StackValueKind kind, uint index)
        {
            OutgoingMessage cmd = CreateMessage_GetValue_Stack(pid, depth, kind, index);

            IncomingMessage reply = PerformSyncRequest(cmd);

            if (reply != null)
            {
                Commands.Debugging_Value_Reply cmdReply = reply.Payload as Commands.Debugging_Value_Reply;

                return RuntimeValue.Convert(this, cmdReply.m_values);
            }

            return null;
        }

        public List<RuntimeValue> GetStackFrameValueAll(uint pid, uint depth, uint cValues, StackValueKind kind)
        {
            List<OutgoingMessage> commands = new List<OutgoingMessage>();
            List<RuntimeValue> vals = new List<RuntimeValue>();

            for (uint i = 0; i < cValues; i++)
            {
                commands.Add(CreateMessage_GetValue_Stack(pid, depth, kind, i));
            }

            List<IncomingMessage> replies = PerformRequestBatch(commands);

            if (replies != null)
            {
                foreach (IncomingMessage message in replies)
                {
                    Commands.Debugging_Value_Reply reply = message.Payload as Commands.Debugging_Value_Reply;
                    if (reply != null)
                    {
                        vals.Add(RuntimeValue.Convert(this, reply.m_values));
                    }
                }
            }

            return vals;
        }

        public RuntimeValue GetArrayElement(uint arrayReferenceId, uint index)
        {
            Commands.Debugging_Value_GetArray cmd = new Commands.Debugging_Value_GetArray();

            cmd.m_heapblock = arrayReferenceId;
            cmd.m_index = index;

            RuntimeValue rtv = GetRuntimeValue(Commands.c_Debugging_Value_GetArray, cmd);

            if (rtv != null)
            {
                rtv.m_handle.m_arrayref_referenceID = arrayReferenceId;
                rtv.m_handle.m_arrayref_index = index;
            }

            return rtv;
        }

        internal bool SetArrayElement(uint heapblock, uint index, byte[] data)
        {
            Commands.Debugging_Value_SetArray cmd = new Commands.Debugging_Value_SetArray();

            cmd.m_heapblock = heapblock;
            cmd.m_index = index;

            data.CopyTo(cmd.m_value, 0);

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Value_SetArray, 0, cmd);

            if (reply != null)
            {
                return reply.IsPositiveAcknowledge();
            }

            return false;
        }

        public RuntimeValue GetScratchPadValue(int index)
        {
            Commands.Debugging_Value_GetScratchPad cmd = new Commands.Debugging_Value_GetScratchPad();

            cmd.m_index = index;

            return GetRuntimeValue(Commands.c_Debugging_Value_GetScratchPad, cmd);
        }

        public RuntimeValue AllocateObject(int scratchPadLocation, uint td)
        {
            Commands.Debugging_Value_AllocateObject cmd = new Commands.Debugging_Value_AllocateObject();

            cmd.m_index = scratchPadLocation;
            cmd.m_td = td;

            return GetRuntimeValue(Commands.c_Debugging_Value_AllocateObject, cmd);
        }

        public RuntimeValue AllocateString(int scratchPadLocation, string val)
        {
            Commands.Debugging_Value_AllocateString cmd = new Commands.Debugging_Value_AllocateString();

            cmd.m_index = scratchPadLocation;
            cmd.m_size = (uint)Encoding.UTF8.GetByteCount(val);

            RuntimeValue rtv = GetRuntimeValue(Commands.c_Debugging_Value_AllocateString, cmd);

            if (rtv != null)
            {
                rtv.SetStringValue(val);
            }

            return rtv;
        }

        public RuntimeValue AllocateArray(int scratchPadLocation, uint td, int depth, int numOfElements)
        {
            Commands.Debugging_Value_AllocateArray cmd = new Commands.Debugging_Value_AllocateArray();

            cmd.m_index = scratchPadLocation;
            cmd.m_td = td;
            cmd.m_depth = (uint)depth;
            cmd.m_numOfElements = (uint)numOfElements;

            return GetRuntimeValue(Commands.c_Debugging_Value_AllocateArray, cmd);
        }

        public Commands.Debugging_Resolve_Type.Result ResolveType(uint td)
        {
            Commands.Debugging_Resolve_Type.Result result = (Commands.Debugging_Resolve_Type.Result)m_typeSysLookup.Lookup(TypeSysLookup.Type.Type, td);

            if (result == null)
            {
                Commands.Debugging_Resolve_Type cmd = new Commands.Debugging_Resolve_Type();

                cmd.m_td = td;

                IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Resolve_Type, 0, cmd);

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

        public Commands.Debugging_Resolve_Method.Result ResolveMethod(uint md)
        {
            Commands.Debugging_Resolve_Method.Result result = (Commands.Debugging_Resolve_Method.Result)m_typeSysLookup.Lookup(TypeSysLookup.Type.Method, md);

            if (result == null)
            {
                Commands.Debugging_Resolve_Method cmd = new Commands.Debugging_Resolve_Method();

                cmd.m_md = md;

                IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Resolve_Method, 0, cmd);

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

        public Commands.Debugging_Resolve_Field.Result ResolveField(uint fd)
        {
            Commands.Debugging_Resolve_Field.Result result = (Commands.Debugging_Resolve_Field.Result)m_typeSysLookup.Lookup(TypeSysLookup.Type.Field, fd);

            if (result == null)
            {
                Commands.Debugging_Resolve_Field cmd = new Commands.Debugging_Resolve_Field();

                cmd.m_fd = fd;

                IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Resolve_Field, 0, cmd);
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

        public Commands.Debugging_Resolve_AppDomain.Reply ResolveAppDomain(uint appDomainID)
        {
            if (!Capabilities.AppDomains)
                return null;

            Commands.Debugging_Resolve_AppDomain cmd = new Commands.Debugging_Resolve_AppDomain();

            cmd.m_id = appDomainID;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Resolve_AppDomain, 0, cmd);

            if (reply != null)
            {
                return reply.Payload as Commands.Debugging_Resolve_AppDomain.Reply;
            }

            return null;
        }

        public string GetTypeName(uint td)
        {
            Commands.Debugging_Resolve_Type.Result resolvedType = ResolveType(td);

            return (resolvedType != null) ? resolvedType.m_name : null;
        }

        public string GetMethodName(uint md, bool fIncludeType)
        {
            Commands.Debugging_Resolve_Method.Result resolvedMethod = ResolveMethod(md);
            string name = null;

            if (resolvedMethod != null)
            {
                if (fIncludeType)
                {
                    name = string.Format("{0}::{1}", GetTypeName(resolvedMethod.m_td), resolvedMethod.m_name);
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
        public (string Td, uint Offset, uint Success) GetFieldName(uint fd)
        {
            Commands.Debugging_Resolve_Field.Result resolvedField = ResolveField(fd);

            if (resolvedField != null)
            {
                return (resolvedField.m_name, resolvedField.m_td, resolvedField.m_offset);
            }

            return (null, 0, 0);
        }

        public uint GetVirtualMethod(uint md, RuntimeValue obj)
        {
            Commands.Debugging_Resolve_VirtualMethod cmd = new Commands.Debugging_Resolve_VirtualMethod();

            cmd.m_md = md;
            cmd.m_obj = obj.ReferenceId;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Resolve_VirtualMethod, 0, cmd);

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

        public void InjectButtons(uint pressed, uint released)
        {
            Commands.Debugging_Button_Inject cmd = new Commands.Debugging_Button_Inject();

            cmd.m_pressed = pressed;
            cmd.m_released = released;

            PerformSyncRequest(Commands.c_Debugging_Button_Inject, 0, cmd);
        }

        public List<ThreadStatus> GetThreads()
        {
            List<ThreadStatus> threads = new List<ThreadStatus>();
            uint[] pids = GetThreadList();

            if (pids != null)
            {
                for (int i = 0; i < pids.Length; i++)
                {
                    Commands.Debugging_Thread_Stack.Reply reply = GetThreadStack(pids[i]);

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
                            ts.m_calls[depth - 1 - j] = String.Format("{0} [IP:{1:X4}]", GetMethodName(reply.m_data[j].m_md, true), reply.m_data[j].m_IP);
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
        public (uint Entrypoint, uint StorageStart, uint StorageLength, bool Success) DeploymentGetStatusWithResult()
        {
            Commands.DebuggingDeploymentStatus.Reply status = DeploymentGetStatus();

            if (status != null)
            {
                return (status.EntryPoint, status.StorageStart, status.StorageLength, true);
            }
            else
            {
                return (0, 0, 0, false);
            }
        }

        public Commands.DebuggingDeploymentStatus.Reply DeploymentGetStatus()
        {
            // TODO replace with token argument
            CancellationTokenSource cancelTSource = new CancellationTokenSource();

            Commands.DebuggingDeploymentStatus cmd = new Commands.DebuggingDeploymentStatus();
            Commands.DebuggingDeploymentStatus.Reply cmdReply = null;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Deployment_Status, Flags.c_NoCaching, cmd);

            if (reply != null)
            {
                cmdReply = reply.Payload as Commands.DebuggingDeploymentStatus.Reply;
            }

            return cmdReply;
        }

        public bool Info_SetJMC(bool fJMC, ReflectionDefinition.Kind kind, uint index)
        {
            Commands.Debugging_Info_SetJMC cmd = new Commands.Debugging_Info_SetJMC();

            cmd.m_fIsJMC = (uint)(fJMC ? 1 : 0);
            cmd.m_kind = (uint)kind;
            cmd.m_raw = index;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Info_SetJMC, 0, cmd);

            if (reply != null)
            {
                return reply.IsPositiveAcknowledge();
            }

            return false;
        }

        private bool DeploymentExecuteIncremental(List<byte[]> assemblies, IProgress<string> progress)
        {
            // get flash sector map from device
            var flashSectorMap = GetFlashSectorMap();

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
                    var eraseResult = EraseMemory((uint)block.StartAddress, 1);
                    if (!eraseResult.Success)
                    {
                        progress?.Report(($"FAILED to erase device memory @0x{block.StartAddress.ToString("X8")} with Length=0x{block.Size.ToString("X8")}"));

                        return false;
                    }

                    var writeResult = WriteMemory((uint)block.StartAddress, block.DeploymentData);
                    if (!writeResult.Success)
                    {
                        progress?.Report(($"FAILED to write device memory @0x{block.StartAddress.ToString("X8")} with Length={block.Size.ToString("X8")}"));

                        return false;
                    }

                    // report progress
                    // progress?.Report($"Deployed assemblies for a total size of {blocksToDeploy.Sum(b => b.Size)} bytes");
                }

                // deployment successful
                return true;
            }

            // invalid flash map
            // TODO provide feedback to user
            return false;
        }

        private bool DeploymentExecuteFull(List<byte[]> assemblies, IProgress<string> progress)
        {
            uint storageStart;
            uint deployLength;
            byte[] closeHeader = new byte[8];

            // perform request
            var reply = DeploymentGetStatusWithResult();

            // check if request was successfully executed
            if (!reply.Success)
            {
                return false;
            }

            // fill in the local properties with the result
            storageStart = reply.StorageStart;

            if (reply.StorageLength == 0)
            {
                return false;
            }

            deployLength = (uint)closeHeader.Length;

            foreach (byte[] assembly in assemblies)
            {
                deployLength += (uint)assembly.Length;
            }

            progress?.Report(string.Format("Deploying assemblies for a total size of {0} bytes", deployLength));

            if (deployLength > reply.StorageLength)
            {
                return false;
            }

            var eraseResult = EraseMemory(storageStart, deployLength);

            if (!eraseResult.Success)
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

                var writeResult1 = WriteMemory(storageStart, assembly);

                if (!writeResult1.Success)
                {
                    return false;
                }

                storageStart += (uint)assembly.Length;
            }

            var writeResult2 = WriteMemory(storageStart, closeHeader);
            if (!writeResult2.Success)
            {
                return false;
            }

            return true;
        }

        //public bool Deployment_Execute(ArrayList assemblies)
        //{
        //    return Deployment_Execute(assemblies, true, null);
        //}

        public bool DeploymentExecute(List<byte[]> assemblies, bool fRebootAfterDeploy = true, IProgress<string> progress = null)
        {
            bool fDeployedOK = false;

            if (!PauseExecution())
            {
                return false;
            }

            if (Capabilities.IncrementalDeployment)
            {
                progress?.Report("Incrementally deploying assemblies to device");

                fDeployedOK = DeploymentExecuteIncremental(assemblies, progress);
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

                    RebootDevice(RebootOptions.ClrOnly);
                }
            }

            return fDeployedOK;
        }

        public (uint Current, bool Success) SetProfilingMode(uint iSet, uint iReset)
        {
            Commands.Profiling_Command cmd = new Commands.Profiling_Command();
            cmd.m_command = Commands.Profiling_Command.c_Command_ChangeConditions;
            cmd.m_parm1 = iSet;
            cmd.m_parm2 = iReset;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Profiling_Command, 0, cmd);
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

        public bool FlushProfilingStream()
        {
            Commands.Profiling_Command cmd = new Commands.Profiling_Command();
            cmd.m_command = Commands.Profiling_Command.c_Command_FlushStream;
            PerformSyncRequest(Commands.c_Profiling_Command, 0, cmd);
            return true;
        }

        private IncomingMessage DiscoverCLRCapability(uint capabilities)
        {
            Commands.Debugging_Execution_QueryCLRCapabilities cmd = new Commands.Debugging_Execution_QueryCLRCapabilities();

            cmd.m_caps = capabilities;

            return PerformSyncRequest(Commands.c_Debugging_Execution_QueryCLRCapabilities, 0, cmd);
        }

        private uint DiscoverCLRCapabilityAsUint(uint capabilities)
        {
            uint ret = 0;

            IncomingMessage reply = DiscoverCLRCapability(capabilities);

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

        private CLRCapabilities.Capability DiscoverCLRCapabilityFlags()
        {
            Debug.WriteLine("DiscoverCLRCapability");

            return (CLRCapabilities.Capability)DiscoverCLRCapabilityAsUint(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityFlags);
        }

        private CLRCapabilities.SoftwareVersionProperties DiscoverSoftwareVersionProperties()
        {
            Debug.WriteLine("DiscoverSoftwareVersionProperties");

            IncomingMessage reply = DiscoverCLRCapability(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilitySoftwareVersion);

            Commands.Debugging_Execution_QueryCLRCapabilities.SoftwareVersion ver = new Commands.Debugging_Execution_QueryCLRCapabilities.SoftwareVersion();

            CLRCapabilities.SoftwareVersionProperties verCaps = new CLRCapabilities.SoftwareVersionProperties();

            if (reply != null)
            {
                Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_QueryCLRCapabilities.Reply;

                if (cmdReply != null && cmdReply.m_data != null)
                {
                    new Converter().Deserialize(ver, cmdReply.m_data);

                    verCaps = new CLRCapabilities.SoftwareVersionProperties(ver.BuildDate, ver.CompilerInfo, ver.CompilerVersion);
                }
            }

            return verCaps;
        }

        private CLRCapabilities.HalSystemInfoProperties DiscoverHalSystemInfoProperties()
        {
            Debug.WriteLine("DiscoverHalSystemInfoProperties");

            IncomingMessage reply = DiscoverCLRCapability(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityHalSystemInfo);

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

        private CLRCapabilities.ClrInfoProperties DiscoverClrInfoProperties()
        {
            Debug.WriteLine("DiscoverClrInfoProperties");

            IncomingMessage reply = DiscoverCLRCapability(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityClrInfo);

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

        private CLRCapabilities.TargetInfoProperties DiscoverTargetInfoProperties()
        {
            Debug.WriteLine("==============================");
            Debug.WriteLine("DiscoverTargetInfoProperties");

            IncomingMessage reply = DiscoverCLRCapability(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilitySolutionReleaseInfo);

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

        /// <summary>
        /// Gets a list of the native assemblies available in the target device.
        /// </summary>
        /// <returns>A list of the native assemblies available in the target device</returns>
        private List<CLRCapabilities.NativeAssemblyProperties> DiscoveryInteropNativeAssemblies()
        {
            IncomingMessage reply = DiscoverCLRCapability(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityInteropNativeAssemblies);

            Commands.Debugging_Execution_QueryCLRCapabilities.NativeAssemblies nativeInteropAssemblies = new Commands.Debugging_Execution_QueryCLRCapabilities.NativeAssemblies();

            if (reply != null)
            {
                if (reply.IsPositiveAcknowledge())
                {
                    Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply = reply.Payload as Commands.Debugging_Execution_QueryCLRCapabilities.Reply;

                    if (cmdReply != null && cmdReply.m_data != null)
                    {
                        new Converter().Deserialize(nativeInteropAssemblies, cmdReply.m_data);

                        return nativeInteropAssemblies.NativeInteropAssemblies.Select(a => new CLRCapabilities.NativeAssemblyProperties(a.Name, a.CheckSum, a.AssemblyVersion.Version)).ToList();
                    }
                }
            }

            // device can't NEVER EVER report that no native assemblies are deployed
            return null;
        }

        private CLRCapabilities DiscoverCLRCapabilities(CancellationToken cancellationToken)
        {
            var clrFlags = DiscoverCLRCapabilityFlags();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            var softwareVersion = DiscoverSoftwareVersionProperties();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            var halSysInfo = DiscoverHalSystemInfoProperties();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            var clrInfo = DiscoverClrInfoProperties();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            var solutionInfo = DiscoverTargetInfoProperties();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            var nativeAssembliesInfo = DiscoveryInteropNativeAssemblies();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            // sanity check for NULL native assemblies
            if (nativeAssembliesInfo == null)
            {
                Debug.WriteLine("device reporting no native assemblies deployed");
                return null;
            }

            return new CLRCapabilities(clrFlags, softwareVersion, halSysInfo, clrInfo, solutionInfo, nativeAssembliesInfo);
        }

        #endregion


        #region Device configuration methods

        public DeviceConfiguration GetDeviceConfiguration(CancellationToken cancellationToken)
        {
            // get all network configuration blocks
            var networkConfigs = GetAllNetworkConfigurations();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            // get all wireless network configuration blocks
            var networkWirelessConfigs = GetAllWireless80211Configurations();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            return new DeviceConfiguration(networkConfigs, networkWirelessConfigs);
        }

        public List<DeviceConfiguration.NetworkConfigurationProperties> GetAllNetworkConfigurations()
        {
            List<DeviceConfiguration.NetworkConfigurationProperties> networkConfigurations = new List<DeviceConfiguration.NetworkConfigurationProperties>();

            DeviceConfiguration.NetworkConfigurationProperties networkConfig = null;
            uint index = 0;

            do
            {
                // get next network configuration block, if available
                networkConfig = GetNetworkConfiguratonProperties(index++);

                // add to list, if valid
                if (!networkConfig.IsUnknown)
                {
                    networkConfigurations.Add(networkConfig);
                }
            }
            while (!networkConfig.IsUnknown);

            return networkConfigurations;
        }

        public List<DeviceConfiguration.Wireless80211ConfigurationProperties> GetAllWireless80211Configurations()
        {
            List<DeviceConfiguration.Wireless80211ConfigurationProperties> wireless80211Configurations = new List<DeviceConfiguration.Wireless80211ConfigurationProperties>();

            DeviceConfiguration.Wireless80211ConfigurationProperties wirelessConfigProperties = null;
            uint index = 0;

            do
            {
                // get next network configuration block, if available
                wirelessConfigProperties = GetWireless80211ConfiguratonProperties(index++);

                // add to list, if valid
                if(!wirelessConfigProperties.IsUnknown)
                {
                    wireless80211Configurations.Add(wirelessConfigProperties);
                }
            }
            while (!wirelessConfigProperties.IsUnknown);

            return wireless80211Configurations;
        }

        public DeviceConfiguration.NetworkConfigurationProperties GetNetworkConfiguratonProperties(uint configurationBlockIndex)
        {
            Debug.WriteLine("NetworkConfiguratonProperties");

            IncomingMessage reply = GetDeviceConfiguration((uint)DeviceConfiguration.DeviceConfigurationOption.Network, configurationBlockIndex);

            Commands.Monitor_QueryConfiguration.NetworkConfiguration networkConfiguration = new Commands.Monitor_QueryConfiguration.NetworkConfiguration();

            DeviceConfiguration.NetworkConfigurationProperties networkConfigProperties = new DeviceConfiguration.NetworkConfigurationProperties();

            if (reply != null)
            {
                if (reply.Payload is Commands.Monitor_QueryConfiguration.Reply cmdReply && cmdReply.Data != null)
                {
                    new Converter().Deserialize(networkConfiguration, cmdReply.Data);

                    // sanity check for invalid configuration (can occur for example when flash is erased and reads as 0xFF)
                    if (networkConfiguration.StartupAddressMode > (byte)AddressMode.AutoIP)
                    {
                        // fix this to invalid
                        networkConfiguration.StartupAddressMode = (byte)AddressMode.Invalid;
                    }

                    networkConfigProperties = new DeviceConfiguration.NetworkConfigurationProperties(networkConfiguration);
                }
            }

            return networkConfigProperties;
        }

        public DeviceConfiguration.Wireless80211ConfigurationProperties GetWireless80211ConfiguratonProperties(uint configurationBlockIndex)
        {
            Debug.WriteLine("NetworkWirelessConfiguratonProperties");

            IncomingMessage reply = GetDeviceConfiguration((uint)DeviceConfiguration.DeviceConfigurationOption.Wireless80211Network, configurationBlockIndex);

            Commands.Monitor_QueryConfiguration.NetworkWirelessConfiguration wirelessConfiguration = new Commands.Monitor_QueryConfiguration.NetworkWirelessConfiguration();

            DeviceConfiguration.Wireless80211ConfigurationProperties wirelessConfigProperties = new DeviceConfiguration.Wireless80211ConfigurationProperties();

            if (reply != null)
            {
                if (reply.Payload is Commands.Monitor_QueryConfiguration.Reply cmdReply && cmdReply.Data != null)
                {
                    new Converter().Deserialize(wirelessConfiguration, cmdReply.Data);

                    wirelessConfigProperties = new DeviceConfiguration.Wireless80211ConfigurationProperties(wirelessConfiguration);
                }
            }

            return wirelessConfigProperties;
        }

        private IncomingMessage GetDeviceConfiguration(uint configuration, uint configurationBlockIndex)
        {
            Commands.Monitor_QueryConfiguration cmd = new Commands.Monitor_QueryConfiguration
            {
                Configuration = configuration,
                BlockIndex = configurationBlockIndex
            };

            return PerformSyncRequest(Commands.c_Monitor_QueryConfiguration, 0, cmd);
        }

        /// <summary>
        /// Writes the full configuration to the device.
        /// This method should be used when the target device stores the configuration in a flash sector.
        /// </summary>
        /// <param name="configuration">The device configuration</param>
        /// <returns></returns>
        public bool UpdateDeviceConfiguration(DeviceConfiguration configuration)
        {
            bool okToUploadConfig = false;

            // the requirement to erase flash before storing is dependent on CLR capabilities which is only available if the device is running nanoCLR
            // when running nanoBooter those are not available
            // currently the only target that doesn't have nanoBooter is ESP32, so we are assuming that the remaining ones (being STM32 based) use internal flash for storing configuration blocks
            // if that is not the case, then the flash map won't show any config blocks and this step will be skipped 
            if ((ConnectionSource == ConnectionSource.nanoCLR && Capabilities.ConfigBlockRequiresErase) ||
                ConnectionSource == ConnectionSource.nanoBooter)
            { 
                // this devices probably requires flash erase before updating the configuration block

                // we need the device memory map in order to know were to store this
                if (FlashSectorMap.Count == 0)
                {
                    // flash sector map is still empty, go get it
                    if (GetFlashSectorMap() == null)
                    {
                        return false;
                    }
                }

                // get configuration sector details
                var configSector = FlashSectorMap.FirstOrDefault(item => (item.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CONFIG);

                // check if the device has a config sector
                if (configSector.m_NumBlocks > 0)
                {
                    // store the current configuration in case we need to revert this for some reason
                    var readConfigSector = ReadMemory(configSector.m_StartAddress, configSector.m_NumBlocks * configSector.m_BytesPerBlock);

                    if (readConfigSector.Success)
                    {
                        // start erasing the sector that holds the configuration block
                        var eraseResult = EraseMemory(configSector.m_StartAddress, 1);
                        if (eraseResult.Success)
                        {
                            okToUploadConfig = true;
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
            }
            else
            {
                // configurations storage doesn't require erase
                okToUploadConfig = true;
            }

            if (okToUploadConfig)
            {
                // serialize the configuration block
                var configurationSerialized = CreateConverter().Serialize(((DeviceConfigurationBase)configuration));

                // prepare command to upload new configuration
                Commands.Monitor_UpdateConfiguration cmd = new Commands.Monitor_UpdateConfiguration
                {
                    Configuration = (uint)DeviceConfiguration.DeviceConfigurationOption.All
                };
                cmd.PrepareForSend(configurationSerialized, configurationSerialized.Length);

                IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_UpdateConfiguration, 0, cmd);

                if (reply != null)
                {
                    Commands.Monitor_UpdateConfiguration.Reply cmdReply = reply.Payload as Commands.Monitor_UpdateConfiguration.Reply;

                    if (reply.IsPositiveAcknowledge() && cmdReply.ErrorCode == 0)
                    {
                        return true;
                    }
                }

                // write failed, try to replace back the old config?
                // FIXME
            }

            // default to false
            return false;
        }

        /// <summary>
        /// Writes a specific configuration block to the device.
        /// The configuration block is updated only with the changes for this configuration part.
        /// </summary>
        /// <param name="configuration">The configuration block</param>
        /// <param name="blockIndex">The index of this configuration block</param>
        /// <returns></returns>
        public bool UpdateDeviceConfiguration<T>(T configuration, uint blockIndex)
        {
            // Create cancellation token source
            CancellationTokenSource cts = new CancellationTokenSource();

            // get the current configuration from the device
            var currentConfiguration = GetDeviceConfiguration(cts.Token);

            if (currentConfiguration != null)
            {
                // now update the specific configuration block
                if (configuration.GetType().Equals(typeof(DeviceConfiguration.NetworkConfigurationProperties)))
                {
                    // validate request index
                    if (currentConfiguration.NetworkConfigurations.ValidateIndex(blockIndex))
                    {
                        // if list is empty and request index is 0
                        if (currentConfiguration.NetworkConfigurations.Count == 0 && blockIndex == 0)
                        {
                            currentConfiguration.NetworkConfigurations.Add(configuration as DeviceConfiguration.NetworkConfigurationProperties);
                        }
                        else
                        {
                            currentConfiguration.NetworkConfigurations[(int)blockIndex] = configuration as DeviceConfiguration.NetworkConfigurationProperties;
                        }
                    }
                }
                else if (configuration.GetType().Equals(typeof(DeviceConfiguration.Wireless80211ConfigurationProperties)))
                {
                    // if list is empty and request index is 0
                    if (currentConfiguration.Wireless80211Configurations.Count == 0 && blockIndex == 0)
                    {
                        currentConfiguration.Wireless80211Configurations.Add(configuration as DeviceConfiguration.Wireless80211ConfigurationProperties);
                    }
                    else
                    {
                        currentConfiguration.Wireless80211Configurations[(int)blockIndex] = configuration as DeviceConfiguration.Wireless80211ConfigurationProperties;
                    }
                }

                if(UpdateDeviceConfiguration(currentConfiguration))
                {
                    // done here
                    return true;
                }
                else
                {
                    // write failed, the old configuration is supposed to have been reverted by 
                }
            }

            // default to false
            return false;
        }

        #endregion

    }
}
