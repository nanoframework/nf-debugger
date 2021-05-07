//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.WireProtocol;
using Polly;
using Polly.Contrib.WaitAndRetry;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
        
        /// <summary>
        /// This constant is to be used to pause between requests, when doing batched requests to a target.
        /// </summary>
        private const int _InterRequestSleep = 2;

        internal IPort _portDefinition;
        internal Controller _controlller { get; set; }

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

        readonly CancellationTokenSource _noiseHandlingCancellation = new CancellationTokenSource();

        AutoResetEvent _rpcEvent;
        //ArrayList m_rpcQueue;
        ArrayList _rpcEndPoints;

        static ManualResetEvent _pingEvent;
        TypeSysLookup m_typeSysLookup;
        EngineState _state;

        private Thread _backgroundProcessor;

        protected readonly WireProtocolRequestsStore _requestsStore = new WireProtocolRequestsStore();
        protected readonly Timer _pendingRequestsTimer;

        internal INanoDevice Device;

        internal Engine(NanoDeviceBase device)
        {
            InitializeLocal(device);

            // default to false
            IsCRC32EnabledForWireProtocol = false;

            _pendingRequestsTimer = new Timer(ClearPendingRequests, null, 0, 100);
        }

        private void Initialize()
        {
            m_notifyEvent = new AutoResetEvent(false);
            _rpcEvent = new AutoResetEvent(false);
            _pingEvent = new ManualResetEvent(false);

            //m_rpcQueue = ArrayList.Synchronized(new ArrayList());
            //m_rpcEndPoints = ArrayList.Synchronized(new ArrayList());
            //m_notifyQueue = ArrayList.Synchronized(new ArrayList());

            m_notifyNoise = new FifoBuffer();
            m_typeSysLookup = new TypeSysLookup();
            _state = new EngineState(this);

            //default capabilities, used until clr can be queried.
            Capabilities = new CLRCapabilities();

            // clear maps
            FlashSectorMap = new List<Commands.Monitor_FlashSectorMap.FlashSectorData>();
            MemoryMap = new List<Commands.Monitor_MemoryMap.Range>();
        }

        private void InitializeLocal(NanoDeviceBase device)
        {
            _portDefinition = device.ConnectionPort;
            _controlller = new Controller(this);

            Device = (INanoDevice)device;

            Initialize();
        }

        public CLRCapabilities Capabilities { get; internal set; }
        public TargetInfo TargetInfo { get; private set; }
        
        public List<Commands.Monitor_FlashSectorMap.FlashSectorData> FlashSectorMap { get; private set; }
        
        public List<Commands.Monitor_MemoryMap.Range> MemoryMap { get; private set; }

        /// <summary>
        /// Default timeout value for operations (in milliseconds).
        /// </summary>
        public int DefaultTimeout { get; set; } = TIMEOUT_DEFAULT;

        public BinaryFormatter CreateBinaryFormatter()
        {
            return new BinaryFormatter(Capabilities);
        }

        /// <summary>
        /// This indicates if the device requires that the configuration block to be erased before updating it.
        /// </summary>
        public bool ConfigBlockRequiresErase { get; private set; }

        /// <summary>
        /// This indicates if the device has a proprietary bootloader.
        /// </summary>
        public bool HasProprietaryBooter { get; private set; }
        
        /// <summary>
        /// This indicates if the target device has nanoBooter.
        /// </summary>
        public bool HasNanoBooter { get; private set; }

        /// <summary>
        ///  This indicates if the target device is IFU capable.
        /// </summary>
        public bool IsIFUCapable { get; private set; }

        public bool IsConnected { get; internal set; }

        private ConnectionSource _connectionSource = ConnectionSource.Unknown;

#pragma warning disable S2292 // Need to keep this here to allow transition period before remove it for good.
        public ConnectionSource ConnectionSource { get => _connectionSource; set => _connectionSource = value; }
#pragma warning restore S2292 // Trivial properties should be auto-implemented

        public bool IsConnectedTonanoCLR => _connectionSource == ConnectionSource.nanoCLR;

        public bool IsConnectedTonanoBooter => _connectionSource == ConnectionSource.nanoBooter;

        public bool IsTargetBigEndian { get; internal set; }

        /// <summary>
        /// This flag is true when connected nanoDevice implements CRC32 in Wire Protocol packets and headers
        /// </summary>
        public bool IsCRC32EnabledForWireProtocol { get; internal set; }

        /// <summary>
        /// Wire Protocol packet size. Default is 1024.
        /// </summary>
        public uint WireProtocolPacketSize { get; internal set; } = 1024;

        public bool StopDebuggerOnConnect { get; set; }

        /// <summary>
        /// Setting for debugger output to be silent.
        /// </summary>
        public bool Silent { get; set; }

        public bool Connect()
        {
            return Connect(
                TIMEOUT_DEFAULT,
                false,
                false);
        }

        public bool Connect(
            bool force,
            bool requestCapabilities = false)
        {
            return Connect(
                TIMEOUT_DEFAULT,
                force,
                requestCapabilities);
        }

        public bool Connect(
            int millisecondsTimeout, 
            bool force = false,
            bool requestCapabilities = false)
        {
            var timeout = millisecondsTimeout != TIMEOUT_DEFAULT ? millisecondsTimeout : DefaultTimeout;

            bool capabilitesRetrieved = false;

            if (force || !IsConnected)
            {
                // connect to device 
                if (Device.Connect())
                {
                    if (!IsRunning)
                    {
                        if (_state.GetValue() == EngineState.Value.NotStarted )
                        {
                            // background processor was never started
                            _state.SetValue(EngineState.Value.Starting, true);

                            // start task to process background messages
                            _backgroundProcessor = CreateThreadHelper(new ThreadStart(this.IncomingMessagesListener));

                            _state.SetValue(EngineState.Value.Started, false);
                        }
                        else
                        {
                            // background processor is not running, start it
                            ResumeProcessing();
                        }
                    }

                    Commands.Monitor_Ping cmd = new Commands.Monitor_Ping
                    {
                        Source = Commands.Monitor_Ping.c_Ping_Source_Host,
                        Flags = (StopDebuggerOnConnect ? Commands.Monitor_Ping.c_Ping_DbgFlag_Stop : 0)
                    };

                    IncomingMessage msg = PerformSyncRequest(Commands.c_Monitor_Ping, Flags.c_NoCaching, cmd, timeout);

                    if (msg == null || msg?.Payload == null)
                    {
                        goto connect_failed;
                    }

                    if (msg.Payload is Commands.Monitor_Ping.Reply reply)
                    {
                        IsTargetBigEndian = (reply.Flags & Commands.Monitor_Ping.c_Ping_DbgFlag_BigEndian).Equals(Commands.Monitor_Ping.c_Ping_DbgFlag_BigEndian);

                        IsCRC32EnabledForWireProtocol = (reply.Flags & Commands.Monitor_Ping.c_Ping_WPFlag_SupportsCRC32).Equals(Commands.Monitor_Ping.c_Ping_WPFlag_SupportsCRC32);

                        // get Wire Protocol packet size
                        switch (reply.Flags & Commands.Monitor_Ping.Monitor_Ping_c_PacketSize_Position)
                        {
                            case Commands.Monitor_Ping.Monitor_Ping_c_PacketSize_0128:
                                WireProtocolPacketSize = 128;
                                break;
                            case Commands.Monitor_Ping.Monitor_Ping_c_PacketSize_0256:
                                WireProtocolPacketSize = 256;
                                break;
                            case Commands.Monitor_Ping.Monitor_Ping_c_PacketSize_0512:
                                WireProtocolPacketSize = 512;
                                break;
                            case Commands.Monitor_Ping.Monitor_Ping_c_PacketSize_1024:
                                WireProtocolPacketSize = 1024;
                                break;

                            default:
                                // unsupported packet size
                                throw new NotSupportedException("Wire Protocol packet size reported by target device is not supported.");
                        }

                        // get other device capabilities
                        ConfigBlockRequiresErase = (reply.Flags & Commands.Monitor_Ping.Monitor_Ping_c_ConfigBlockRequiresErase).Equals(Commands.Monitor_Ping.Monitor_Ping_c_ConfigBlockRequiresErase);

                        HasProprietaryBooter = (reply.Flags & Commands.Monitor_Ping.Monitor_Ping_c_HasProprietaryBooter).Equals(Commands.Monitor_Ping.Monitor_Ping_c_HasProprietaryBooter);

                        HasNanoBooter = (reply.Flags & Commands.Monitor_Ping.Monitor_Ping_c_HasNanoBooter).Equals(Commands.Monitor_Ping.Monitor_Ping_c_HasNanoBooter);

                        IsIFUCapable = (reply.Flags & Commands.Monitor_Ping.Monitor_Ping_c_IFUCapable).Equals(Commands.Monitor_Ping.Monitor_Ping_c_IFUCapable);


                        // update flag
                        IsConnected = true;

                        // update field
                        switch (reply.Source)
                        {
                            case Commands.Monitor_Ping.c_Ping_Source_NanoCLR:
                                _connectionSource = ConnectionSource.nanoCLR;
                                break;

                            case Commands.Monitor_Ping.c_Ping_Source_NanoBooter:
                                _connectionSource = ConnectionSource.nanoBooter;
                                break;

                            default:
                                _connectionSource = ConnectionSource.Unknown;
                                break;
                        }

                        if (Silent)
                        {
                            SetExecutionMode(Commands.DebuggingExecutionChangeConditions.State.DebuggerQuiet, 0);
                        }
                    }
                }
            }

            var flashMapPolicy = Policy.Handle<Exception>().OrResult<List<Commands.Monitor_FlashSectorMap.FlashSectorData>>(r => r == null)
                                        .WaitAndRetry(2, retryAttempt => TimeSpan.FromMilliseconds((retryAttempt + 1) * 250));
            var memoryMapPolicy = Policy.Handle<Exception>().OrResult<List<Commands.Monitor_MemoryMap.Range>>(r => r == null)
                                        .WaitAndRetry(2, retryAttempt => TimeSpan.FromMilliseconds((retryAttempt + 1) * 250));
            var targetInfoPolicy = Policy.Handle<Exception>().OrResult<TargetInfo>(r => r == null)
                                            .WaitAndRetry(2, retryAttempt => TimeSpan.FromMilliseconds((retryAttempt + 1) * 250));

            if (IsConnectedTonanoCLR &&
                requestCapabilities)
            {
                CancellationTokenSource cancellationTSource = new CancellationTokenSource();
                CLRCapabilities tempCapabilities = new();

                // fill flash sector map
                if (FlashSectorMap.Count == 0)
                {
                    FlashSectorMap = flashMapPolicy.Execute(() => GetFlashSectorMap());
                }

                if (FlashSectorMap.Count != 0)
                {
                    if(MemoryMap.Count == 0)
                    {
                        MemoryMap = memoryMapPolicy.Execute(() => GetMemoryMap());
                    }

                    if (MemoryMap.Count != 0)
                    {
                        // get target info
                        if (TargetInfo == null)
                        {
                            TargetInfo = targetInfoPolicy.Execute(() => GetMonitorTargetInfo());
                        }

                        if (TargetInfo != null)
                        {
                            if (Capabilities.IsUnknown)
                            {
                                tempCapabilities = DiscoverCLRCapabilities(cancellationTSource.Token);

                                if (tempCapabilities != null
                                    && !tempCapabilities.IsUnknown)
                                {
                                    Capabilities = tempCapabilities;
                                    _controlller.Capabilities = Capabilities;
                                }
                            }

                            if (!Capabilities.IsUnknown)
                            {
                                // we have everything that we need
                                capabilitesRetrieved = true;
                            }

                        }
                    }
                }

                if (requestCapabilities
                    && !capabilitesRetrieved)
                {
                    goto connect_failed;
                }
            }

            if (IsConnectedTonanoBooter &&
                requestCapabilities)
            {
                // fill flash sector map
                if (FlashSectorMap.Count == 0)
                {
                    FlashSectorMap = flashMapPolicy.Execute(() => GetFlashSectorMap());
                }

                if (FlashSectorMap.Count > 0)
                {
                    // flash map available
                    // get target info
                    if (TargetInfo == null)
                    {
                        TargetInfo = targetInfoPolicy.Execute(() => GetMonitorTargetInfo());
                    }

                    if(TargetInfo != null)
                    {
                        // we have everything that we need
                        capabilitesRetrieved = true;
                    }
                }
            }

            if (requestCapabilities &&
                !capabilitesRetrieved)
            {
                goto connect_failed;
            }

            return IsConnected;

        connect_failed:

            // update flag
            IsConnected = false;

            // done here
            return false;
        }

        public bool UpdateDebugFlags()
        {
            if (IsConnected)
            {
                Commands.Monitor_Ping cmd = new Commands.Monitor_Ping
                {
                    Source = Commands.Monitor_Ping.c_Ping_Source_Host,
                    Flags = (StopDebuggerOnConnect ? Commands.Monitor_Ping.c_Ping_DbgFlag_Stop : 0)
                };

                IncomingMessage msg = PerformSyncRequest(Commands.c_Monitor_Ping, Flags.c_NoCaching, cmd);

                if (msg == null || msg?.Payload == null)
                {
                    // update flag
                    IsConnected = false;

                    // done here
                    return false;
                }

                // get state flags to set
                var stateFlagsToSet = Commands.DebuggingExecutionChangeConditions.State.SourceLevelDebugging;

                if (Silent)
                {
                    stateFlagsToSet |= Commands.DebuggingExecutionChangeConditions.State.DebuggerQuiet;
                }

                SetExecutionMode(stateFlagsToSet, 0);

                // done here
                return true;
            }

            // device isn't connected
            return false;
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

                wireProtocolRequest.RequestAborted();

                // remove the request from the store
                if (_requestsStore.Remove(wireProtocolRequest.OutgoingMessage.Header) && requestCanceled)
                {
                    // TODO 
                    // invoke the event hook
                }
            }
        }

        public void IncomingMessagesListener()
        {
            Debug.WriteLine(">>>> START IncomingMessagesListener <<<<");
            
            var reassembler = new MessageReassembler(_controlller);

            while (_state.IsRunning)
            {
                try
                {
                    reassembler.Process();
                }
                catch(DeviceNotConnectedException)
                {
                    Stop(true);
                    break;
                }
                catch(ThreadAbortException)
                {
                    break;
                }
#if DEBUG
                catch (Exception ex)
                {
                    Debug.WriteLine($"IncomingMessagesListener >>>>>>> {ex.Message}");
#else
                catch (Exception)
                {
#endif
                    Stop();
                    break;
                }
            }

            Debug.WriteLine("<<<< EXIT IncomingMessagesListener >>>>");
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

        public bool ThrowOnCommunicationFailure { get; set; } = false;

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
                try
                {
                    Stop();

                    if (_state.SetValue(EngineState.Value.Disposing))
                    {
                        IDisposable disposable = _controlller as IDisposable;

                        if (disposable != null)
                        {
                            disposable.Dispose();
                        }

                        _state.SetValue(EngineState.Value.Disposed);
                    }
                }
                catch
                {
                    // intended use to let dispose happen even if exceptions occur
                }

                disposedValue = true;
            }
        }

        ~Engine()
        {
            Dispose(false);
        }

        public void Dispose()
        {
            Dispose(true);
        }

        #endregion

        /// <summary>
        /// Global lock object for synchronizing message request. This ensures there is only one
        /// outstanding request at any point of time. 
        /// </summary>
        internal object _syncReqLock = new object();

        private IncomingMessage PerformSyncRequest(uint command, uint flags, object payload, int millisecondsTimeout = TIMEOUT_DEFAULT)
        {
            lock (_syncReqLock)
            {
                OutgoingMessage message = new OutgoingMessage(_controlller.GetNextSequenceId(), CreateConverter(), command, flags, payload);

                var timeout = millisecondsTimeout != TIMEOUT_DEFAULT ? millisecondsTimeout : DefaultTimeout;

                var request = PerformRequestAsync(message, timeout);

                try
                {
                    if (request != null)
                    {
                        Task.WaitAll(request);
                    }
                    else
                    {
                        return null;
                    }
                }
                catch (AggregateException)
                {
                    return null;
                }

                return request.Result;
            }
        }

        private IncomingMessage PerformSyncRequest(OutgoingMessage message, int millisecondsTimeout = TIMEOUT_DEFAULT)
        {
            lock (_syncReqLock)
            {
                var timeout = millisecondsTimeout != TIMEOUT_DEFAULT ? millisecondsTimeout : DefaultTimeout;

                var request = PerformRequestAsync(message, timeout);

                try
                {
                    if (request != null)
                    {
                        Task.WaitAll(request);
                    }
                    else
                    {
                        return null;
                    }
                }
                catch
                {
                    // catch everything, doesn't matter
                    return null;
                }

                return request.Result;
            }
        }

        internal Task<IncomingMessage> PerformRequestAsync(OutgoingMessage message, int millisecondsTimeout = TIMEOUT_DEFAULT)
        {
            var timeout = millisecondsTimeout != TIMEOUT_DEFAULT ? millisecondsTimeout : DefaultTimeout;

            WireProtocolRequest request = new WireProtocolRequest(message, timeout);

            // only add this to store if a reply is expected/required
            if (request.NeedsReply)
            {
                _requestsStore.Add(request);
            }

            try
            {
                // Start a background task that will complete tcs1.Task
                Task.Factory.StartNew(() =>
                {
                    if (!request.PerformRequest(_controlller))
                    {
                        // send failed...
                        request.TaskCompletionSource.SetException(new InvalidOperationException());
                    }
                });
            }
            catch (Exception ex)
            {
                // perform request failed, remove it from store
                _requestsStore.Remove(request.OutgoingMessage.Header);

                // report failure
                request.RequestAborted();

                request.TaskCompletionSource.SetException(ex);

                return Task.FromResult<IncomingMessage>(null);
            }

            return request.TaskCompletionSource.Task;
        }

        private List<IncomingMessage> PerformRequestBatch(List<OutgoingMessage> messages)
        {
            List<IncomingMessage> replies = new List<IncomingMessage>();

            foreach (OutgoingMessage message in messages)
            {
                replies.Add(PerformSyncRequest(message));
            }

            return replies;
        }

        public ConnectionSource GetConnectionSource()
        {

            if (!IsConnected)
            {
                if (Connect(TIMEOUT_DEFAULT, true))
                {
                    return ConnectionSource;
                }
                else
                {
                    return ConnectionSource.Unknown;
                }
            }

            IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_Ping, Flags.c_NoCaching, new byte[0]);

            if (reply != null)
            {
                var connectionSource = reply.Payload as Commands.Monitor_Ping.Reply;

                // update field
                _connectionSource = connectionSource.Source switch
                {
                    Commands.Monitor_Ping.c_Ping_Source_NanoCLR => ConnectionSource.nanoCLR,
                    Commands.Monitor_Ping.c_Ping_Source_NanoBooter => ConnectionSource.nanoBooter,
                    _ => ConnectionSource.Unknown,
                };

                return _connectionSource;
            }

            return ConnectionSource.Unknown;
        }

        internal Converter CreateConverter()
        {
            return new Converter(Capabilities);
        }

        public int SendBuffer(byte[] buffer)
        {
            return _portDefinition.SendBuffer(buffer);
        }

        internal WireProtocolRequest FindRequest(Packet packet)
        {
            return _requestsStore.GetByReplyHeader(packet);
        }

        public bool ProcessMessage(IncomingMessage message, bool isReply)
        {
            message.Payload = Commands.ResolveCommandToPayload(message.Header.Cmd, isReply, _controlller.Capabilities);


            if (isReply)
            {
                // we are processing a message flagged as a reply
                // OR we this is a reply to an explicit request

                WireProtocolRequest reply = _requestsStore.GetByReplyHeader(message.Header);

                if (reply != null)
                {
                    // this a reply to a request: remove it from store
                    _requestsStore.Remove(reply.OutgoingMessage.Header);

                    // resolve the response
                    reply.TaskCompletionSource.TrySetResult(message);

                    return true;
                }
            }
            else
            {
                Packet bp = message.Header;

                switch (bp.Cmd)
                {
                    case Commands.c_Monitor_Ping:
                        {
                            Commands.Monitor_Ping.Reply cmdReply = new Commands.Monitor_Ping.Reply
                            {
                                Source = Commands.Monitor_Ping.c_Ping_Source_Host,
                                Flags = (StopDebuggerOnConnect ? Commands.Monitor_Ping.c_Ping_DbgFlag_Stop : 0)
                            };

                            PerformRequestAsync(
                                new OutgoingMessage(
                                    _controlller.GetNextSequenceId(),
                                    message,
                                    _controlller.CreateConverter(),
                                    Flags.c_NonCritical,
                                    cmdReply)
                                );

                            // signal that a monitor ping was received
                            _pingEvent.Set();

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
                        Task.Factory.StartNew(() => RpcReceiveQuery(message, (Commands.Debugging_Messaging_Query)message.Payload));
                        break;

                    case Commands.c_Debugging_Messaging_Reply:
                        Debug.Assert(message.Payload != null);
                        Task.Factory.StartNew(() => RpcReceiveReplyAsync(message, (Commands.Debugging_Messaging_Reply)message.Payload));
                        break;

                    case Commands.c_Debugging_Messaging_Send:
                        Debug.Assert(message.Payload != null);
                        Task.Factory.StartNew(() => RpcReceiveSendAsync(message, (Commands.Debugging_Messaging_Send)message.Payload));
                        break;
                }
            }

            if (_eventCommand != null)
            {
                Task.Factory.StartNew(() => _eventCommand.Invoke(message, isReply));

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

            _eventProcessExit?.Invoke(this, EventArgs.Empty);
        }

        public byte[] ReadBuffer(int bytesToRead)
        {
            return _portDefinition.ReadBuffer(bytesToRead);
        }

        public int AvailableBytes => _portDefinition.AvailableBytes;

        private OutgoingMessage CreateMessage(uint cmd, uint flags, object payload)
        {
            return new OutgoingMessage(_controlller.GetNextSequenceId(), CreateConverter(), cmd, flags, payload);
        }

        public void StopProcessing()
        {
            _state.SetValue(EngineState.Value.Stopping, false);

            if (_backgroundProcessor != null)
            {
                try
                {
                    _backgroundProcessor.Join(TimeSpan.FromSeconds(1));
                    _backgroundProcessor.Abort();
                }
                catch
                {
                    // there are random issues with the cancellation tokens
                }
                finally
                {
                    Thread.Sleep(500);
                    _backgroundProcessor = null;
                }
            }
        }

        public void ResumeProcessing()
        {
            _state.SetValue(EngineState.Value.Resume, false);

            if (_backgroundProcessor == null)
            {
                _backgroundProcessor = CreateThreadHelper(new ThreadStart( this.IncomingMessagesListener ));
            }
        }

        public void Stop(bool force = false)
        {
            if (_state.SetValue(EngineState.Value.Stopping, false))
            {
                StopProcessing();

                Device.Disconnect(force);

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

                if (_controlller is IControllerRemote remote)
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

                if (_controlller is IControllerRemote remote)
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

                    if (ep._type == type && 
                        ep._id == id &&
                        (!fOnlyServer || 
                        ep.IsRpcServer))
                    {
                        return eep;
                    }
                }
            }
            return null;
        }

        private void RpcReceiveQuery(IncomingMessage message, Commands.Debugging_Messaging_Query query)
        {
            Commands.Debugging_Messaging_Address addr = query.m_addr;
            EndPointRegistration eep = RpcFind(addr.m_to_Type, addr.m_to_Id, true);

            Commands.Debugging_Messaging_Query.Reply reply = new Commands.Debugging_Messaging_Query.Reply
            {
                m_found = (eep != null) ? 1u : 0u,
                m_addr = addr
            };

            PerformRequestAsync(new OutgoingMessage(_controlller.GetNextSequenceId(), message, CreateConverter(), Flags.c_NonCritical, reply));
        }

        internal bool RpcCheck(Commands.Debugging_Messaging_Address addr)
        {
            Commands.Debugging_Messaging_Query cmd = new Commands.Debugging_Messaging_Query
            {
                m_addr = addr
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Messaging_Query, 0, cmd);
            if (reply != null)
            {

                if (reply.Payload is Commands.Debugging_Messaging_Query.Reply res && res.m_found != 0)
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

                Commands.Debugging_Messaging_Send cmd = new Commands.Debugging_Messaging_Send
                {
                    m_addr = addr,
                    m_data = data
                };

                IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Messaging_Send, 0, cmd);
                if (reply != null)
                {

                    if (reply.Payload is Commands.Debugging_Messaging_Send.Reply res && res.m_found != 0)
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

            Commands.Debugging_Messaging_Send.Reply res = new Commands.Debugging_Messaging_Send.Reply
            {
                m_found = (eep != null) ? 1u : 0u,
                m_addr = addr
            };

            await PerformRequestAsync(new OutgoingMessage(_controlller.GetNextSequenceId(), msg, CreateConverter(), Flags.c_NonCritical, res));

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
            Commands.Debugging_Messaging_Reply cmd = new Commands.Debugging_Messaging_Reply
            {
                m_addr = addr,
                m_data = data
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Messaging_Reply, 0, cmd);
            if (reply != null)
            {
                Commands.Debugging_Messaging_Reply.Reply res = new Commands.Debugging_Messaging_Reply.Reply();

                if (res?.m_found != 0)
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

            Commands.Debugging_Messaging_Reply.Reply res = new Commands.Debugging_Messaging_Reply.Reply
            {
                m_found = (eep != null) ? 1u : 0u,
                m_addr = addr
            };

            await PerformRequestAsync(new OutgoingMessage(_controlller.GetNextSequenceId(), message, CreateConverter(), Flags.c_NonCritical, res));

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


        internal WireProtocolRequest AsyncRequest(OutgoingMessage message, int timeout)
        {
            WireProtocolRequest request = new WireProtocolRequest(message);

            //Checking whether IsRunning and adding the request to m_requests
            //needs to be atomic to avoid adding a request after the Engine
            //has been stopped.

            if (!IsRunning)
            {
                request.TaskCompletionSource.SetException(new InvalidOperationException());
                return request;
            }

            try
            {
                if (request.PerformRequest(_controlller))
                {
                    _requestsStore.Add(request);
                }
                else
                { 
                    // send failed...
                    request.TaskCompletionSource.SetException(new InvalidOperationException());
                }
            }
            catch
            {
                throw;
            }

            return request;
        }

        //private WireProtocolRequest AsyncMessage(uint command, uint flags, object payload, int timeout)
        //{
        //    OutgoingMessage msg = CreateMessage(command, flags, payload);

        //    return AsyncRequest(msg, timeout);
        //}

        //private IncomingMessage MessageAsync(uint command, uint flags, object payload, int timeout)
        //{
        //    _ = await AsyncMessage(command, flags, payload, timeout);

        //    return null;
        //}

        //private async Task<IncomingMessage> SyncMessageAsync(uint command, uint flags, object payload)
        //{
        //    return await MessageAsync(command, flags, payload, DefaultTimeout);
        //}


#region Commands implementation

        private List<Commands.Monitor_MemoryMap.Range> GetMemoryMap()
        {
            Commands.Monitor_MemoryMap cmd = new Commands.Monitor_MemoryMap();

            IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_MemoryMap, 0, cmd);

            if (reply?.Payload is Commands.Monitor_MemoryMap.Reply cmdReply)
            {
                return cmdReply.m_map;
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

                if (reply.Payload is Commands.Monitor_DeploymentMap.Reply cmdReply)
                {
                    return cmdReply.m_map;
                }
            }

            return null;
        }

        public ReleaseInfo GetMonitorOemInfo()
        {
            Commands.Monitor_OemInfo cmd = new Commands.Monitor_OemInfo();

            IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_OemInfo, 0, cmd);

            if (reply != null)
            {
                if (reply.Payload is Commands.Monitor_OemInfo.Reply)
                {
                    Commands.Monitor_OemInfo.Reply cmdReply = reply.Payload as Commands.Monitor_OemInfo.Reply;

                    return cmdReply.m_releaseInfo;
                }
            }

            return null;
        }

        public TargetInfo GetMonitorTargetInfo()
        {
            Commands.Monitor_TargetInfo cmd = new Commands.Monitor_TargetInfo();

            IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_TargetInfo, 0, cmd);

            if (reply != null)
            {
                if (reply.Payload is Commands.Monitor_TargetInfo.Reply)
                {
                    Commands.Monitor_TargetInfo.Reply cmdReply = reply.Payload as Commands.Monitor_TargetInfo.Reply;

                    return cmdReply.TargetInfo;
                }
            }

            return null;
        }

        private List<Commands.Monitor_FlashSectorMap.FlashSectorData> GetFlashSectorMap()
        {
            IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_FlashSectorMap, 0, null);

            if (reply != null)
            {

                if (reply.Payload is Commands.Monitor_FlashSectorMap.Reply cmdReply)
                {
                    // update property
                    FlashSectorMap = cmdReply.m_map;


                    return cmdReply.m_map;
                }
            }

            return new List<Commands.Monitor_FlashSectorMap.FlashSectorData>();
        }

        private (byte[] Buffer, uint ErrorCode, bool Success) ReadMemory(uint address, uint length, uint offset)
        {
            byte[] buffer = new byte[length];

            while (length > 0)
            {
                Commands.Monitor_ReadMemory cmd = new Commands.Monitor_ReadMemory
                {
                    m_address = address,
                    m_length = length
                };

                IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_ReadMemory, 0, cmd);

                if (reply != null)
                {
                    Commands.Monitor_ReadMemory.Reply cmdReply = reply.Payload as Commands.Monitor_ReadMemory.Reply;

                    if (!reply.IsPositiveAcknowledge() || cmdReply.ErrorCode != 0)
                    {
                        return (new byte[0], cmdReply.ErrorCode, false);
                    }

                    uint actualLength = Math.Min((uint)cmdReply.m_data.Length, length);

                    Array.Copy(cmdReply.m_data, 0, buffer, (int)offset, (int)actualLength);

                    address += actualLength;
                    length -= actualLength;
                    offset += actualLength;
                }
                else
                {
                    return (new byte[0], 0, false);
                }
            }

            return (buffer, 0, true);
        }

        public (byte[] Buffer, uint ErrorCode, bool Success) ReadMemory(uint address, uint length)
        {
            return ReadMemory(address, length, 0);
        }

        public (AccessMemoryErrorCodes ErrorCode, bool Success) WriteMemory(
            uint address,
            byte[] buf,
            int offset,
            int length,
            int programAligment = 0,
            int startLenght = 0,
            int totalLenght = 0,
            IProgress<MessageWithProgress> progress = null,
            IProgress<string> log = null)
        {
            int count = length;
            int position = offset;

            while (count > 0)
            {
                Commands.Monitor_WriteMemory cmd = new Commands.Monitor_WriteMemory();

                // get packet length, either the maximum allowed size or whatever is still available to TX
                int packetLength = Math.Min(GetPacketMaxLength(cmd), count);

                if(programAligment != 0 && packetLength % programAligment != 0)
                {
                    packetLength -= packetLength % programAligment;
                }

                cmd.PrepareForSend(address, buf, position, packetLength);

                DebuggerEventSource.Log.EngineWriteMemory(address, packetLength);

                IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_WriteMemory, 0, cmd);

                if (reply != null)
                {
                    Commands.Monitor_WriteMemory.Reply cmdReply = reply.Payload as Commands.Monitor_WriteMemory.Reply;

                    if (!reply.IsPositiveAcknowledge() ||
                        (AccessMemoryErrorCodes)cmdReply.ErrorCode != AccessMemoryErrorCodes.NoError)
                    {
                        progress?.Report(new MessageWithProgress(""));
                        log?.Report($"Error writing to device @ 0x{address:X8} ({packetLength} bytes). Error code: {cmdReply.ErrorCode}.");

                        return ((AccessMemoryErrorCodes)cmdReply.ErrorCode, false);
                    }

                    address += (uint)packetLength;
                    count -= packetLength;
                    position += packetLength;
                }
                else
                {
                    progress?.Report(new MessageWithProgress(""));
                    log?.Report($"Error writing to device @ 0x{address:X8} ({packetLength} bytes). Unknown error.");

                    return (AccessMemoryErrorCodes.Unknown, false);
                }

                progress?.Report(new MessageWithProgress($"Deployed { startLenght + position }/{totalLenght} bytes...", (uint)(startLenght + position), (uint)totalLenght));
                log?.Report($"Deployed { startLenght + position }/{ totalLenght } bytes.");
            }

            return (AccessMemoryErrorCodes.NoError, true);
        }

        public (AccessMemoryErrorCodes ErrorCode, bool Success) WriteMemory(
            uint address, 
            byte[] buf,
            int programAligment = 0,
            int startLenght = 0,
            int totalLenght = 0,
            IProgress<MessageWithProgress> progress = null,
            IProgress<string> log = null)
        {
            return WriteMemory(
                address,
                buf,
                0,
                buf.Length,
                programAligment,
                startLenght,
                totalLenght,
                progress);
        }

        public bool CheckMemory(uint address, byte[] buf, int offset, int length)
        {
            Commands.Monitor_CheckMemory cmd = new Commands.Monitor_CheckMemory();

            cmd.m_address = address;
            cmd.m_length = (uint)length;

            IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_CheckMemory, 0, cmd);

            if (reply != null)
            {
                Commands.Monitor_CheckMemory.Reply cmdReply = reply.Payload as Commands.Monitor_CheckMemory.Reply;

                if (reply.IsPositiveAcknowledge())
                {
                    // compute CRC32 of the buffer length and compare with the return value
                    return CRC.ComputeCRC(buf, offset, length, 0) == cmdReply.m_crc;
                }
            }

            return false;
        }

        public bool CheckMemory(uint address, byte[] buf)
        {
            return CheckMemory(address, buf, 0, buf.Length);
        }

        public (AccessMemoryErrorCodes ErrorCode, bool Success) EraseMemory(
            uint address, 
            uint length)
        {
            DebuggerEventSource.Log.EngineEraseMemory(address, length);

            var cmd = new Commands.Monitor_EraseMemory
            {
                m_address = address,
                m_length = length
            };

            // 2kB sector:  25ms 
            const int eraseTimeout2kSector = 25;

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


            if (length <= (2 * 1024))
            {
                // timeout for 2kB sector
                timeout = eraseTimeout2kSector + extraTimeoutForErase;
            }
            else if (length <= (16 * 1024))
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

            // minimum timeout required for ESP32
            if(TargetInfo.PlatformName.StartsWith("ESP32"))
            {
                timeout = 10000;
            }

            var getExecutionModePolicy = Policy.Handle<Exception>().OrResult<IncomingMessage>(r => r == null)
                                       .WaitAndRetry(3, retryAttempt => TimeSpan.FromMilliseconds((retryAttempt + 1) * 250));

            var reply = getExecutionModePolicy.Execute(() => PerformSyncRequest(Commands.c_Monitor_EraseMemory, 0, cmd, timeout));

            if (reply != null)
            {
                Commands.Monitor_EraseMemory.Reply cmdReply = reply.Payload as Commands.Monitor_EraseMemory.Reply;

                return ((AccessMemoryErrorCodes)cmdReply.ErrorCode, reply.IsPositiveAcknowledge());
            }

            return (AccessMemoryErrorCodes.Unknown, false);
        }

        public bool ExecuteMemory(uint address)
        {
            Commands.Monitor_Execute cmd = new Commands.Monitor_Execute
            {
                m_address = address
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_Execute, 0, cmd);

            return reply != null && reply.IsPositiveAcknowledge();
        }

        public void RebootDevice(
            RebootOptions options = RebootOptions.NormalReboot,
            IProgress<string> log = null)
        {
            Commands.MonitorReboot cmd = new Commands.MonitorReboot();

            bool fThrowOnCommunicationFailureSav = ThrowOnCommunicationFailure;

            ThrowOnCommunicationFailure = false;

            // check if device running nanoBooter or if it can handle soft reboot
            if (IsConnectedTonanoBooter || 
                Capabilities.SoftReboot)
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
                log?.Report($"Rebooting ({(RebootOptions)cmd.flags})");
                
                // reset event
                _pingEvent.Reset();

                // don't keep hopes too high on a reply from reboot request
                var reqResult = PerformSyncRequest(Commands.c_Monitor_Reboot, Flags.c_NoCaching, cmd);

                if(reqResult != null)
                {
                    var result = reqResult.IsPositiveAcknowledge() ? "successfully" : " without reply";

                    log?.Report($"Reboot command executed {result}");
                }

                // if reboot options ends up on a hard reboot, force connection state to disconnected
                // a Connect request has to happen after this
                if (((RebootOptions)cmd.flags == RebootOptions.EnterNanoBooter) ||
                    ((RebootOptions)cmd.flags == RebootOptions.NormalReboot))
                {
                    IsConnected = false;
                }

                var rebootTimeout = 3 * DefaultTimeout;

                // wait for ping after reboot
                log?.Report($"Waiting {rebootTimeout}ms for reboot...");

                // make it 3x the default timeout
                if(_pingEvent.WaitOne(rebootTimeout))
                {
                    log?.Report($"!! Device reboot confirmed !!");
                }
                else
                {
                    log?.Report($"ERROR: No activity detected from device after reboot...");
                }
            }
#if DEBUG
            catch (Exception ex)
            {
                Debug.WriteLine($"*** ERROR when waiting for reboot operation to complete {ex.Message}");
            }
#endif
            finally
            {
                ThrowOnCommunicationFailure = fThrowOnCommunicationFailureSav;
            }
        }

        public bool Reconnect(bool fSoftReboot, int millisecondsTimeout = TIMEOUT_DEFAULT)
        {
            var timeout = millisecondsTimeout != TIMEOUT_DEFAULT ? millisecondsTimeout : DefaultTimeout;

            if (!Connect(
                timeout,
                true))
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

                if (reply.Payload is Commands.Debugging_Execution_BasePtr.Reply cmdReply)
                {
                    return cmdReply.m_EE;
                }
            }

            return 0;
        }

        public Commands.DebuggingExecutionChangeConditions.State GetExecutionMode()
        {
            Commands.DebuggingExecutionChangeConditions cmd = new Commands.DebuggingExecutionChangeConditions
            {

                // setting these to 0 won't change anything in the target when the command is executed
                // BUT, because the current state is returned we can parse the return result to get the state
                FlagsToSet = 0,
                FlagsToReset = 0
            };

            // need to use a reasonably larger timeout because the device could be sending boot PINGs after a reboot
            // and a timeout too short will miss the reply
            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_ChangeConditions, Flags.c_NoCaching, cmd, 6000);
            if (reply != null)
            {
                // need this to get DebuggingExecutionChangeConditions.State enum from raw value

                if (reply.Payload is Commands.DebuggingExecutionChangeConditions.Reply cmdReply)
                {
                    return (Commands.DebuggingExecutionChangeConditions.State)cmdReply.CurrentState;
                }
            }

            // default to unknown
            return Commands.DebuggingExecutionChangeConditions.State.Unknown;
        }

        public bool SetExecutionMode(Commands.DebuggingExecutionChangeConditions.State flagsToSet, Commands.DebuggingExecutionChangeConditions.State flagsToReset)
        {
            Commands.DebuggingExecutionChangeConditions cmd = new Commands.DebuggingExecutionChangeConditions
            {
                FlagsToSet = (uint)flagsToSet,
                FlagsToReset = (uint)flagsToReset
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_ChangeConditions, Flags.c_NoCaching, cmd);
            if (reply != null)
            {

                if (reply.Payload is Commands.DebuggingExecutionChangeConditions.Reply cmdReply)
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
            Commands.Debugging_Execution_SetCurrentAppDomain cmd = new Commands.Debugging_Execution_SetCurrentAppDomain
            {
                m_id = id
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_SetCurrentAppDomain, 0, cmd);

            return reply != null && reply.IsPositiveAcknowledge();
        }

        public bool SetBreakpoints(Commands.Debugging_Execution_BreakpointDef[] breakpoints)
        {
            Commands.Debugging_Execution_Breakpoints cmd = new Commands.Debugging_Execution_Breakpoints
            {
                m_data = breakpoints
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_Breakpoints, 0, cmd);

            return reply != null && reply.IsPositiveAcknowledge();
        }

        public Commands.Debugging_Execution_BreakpointDef GetBreakpointStatus()
        {
            Commands.Debugging_Execution_BreakpointStatus cmd = new Commands.Debugging_Execution_BreakpointStatus();

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_BreakpointStatus, 0, cmd);

            if (reply != null)
            {

                if (reply.Payload is Commands.Debugging_Execution_BreakpointStatus.Reply cmdReply)
                    return cmdReply.m_lastHit;
            }

            return null;
        }

        public bool SetSecurityKey(byte[] key)
        {
            Commands.Debugging_Execution_SecurityKey cmd = new Commands.Debugging_Execution_SecurityKey
            {
                m_key = key
            };

            return PerformSyncRequest(Commands.c_Debugging_Execution_SecurityKey, 0, cmd) != null;
        }

        public bool UnlockDevice(byte[] blob)
        {
            Commands.Debugging_Execution_Unlock cmd = new Commands.Debugging_Execution_Unlock();

            Array.Copy(blob, 0, cmd.m_command, 0, 128);
            Array.Copy(blob, 128, cmd.m_hash, 0, 128);

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_Unlock, 0, cmd);

            return reply != null && reply.IsPositiveAcknowledge();
        }

        public (uint Address, bool Success) AllocateMemory(uint size)
        {
            Commands.Debugging_Execution_Allocate cmd = new Commands.Debugging_Execution_Allocate
            {
                m_size = size
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Execution_Allocate, 0, cmd);
            if (reply != null)
            {

                if (reply.Payload is Commands.Debugging_Execution_Allocate.Reply cmdReply)
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

        //public bool UpgradeConnectionToSSL_End(IAsyncResult iar)
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

            Commands.Debugging_UpgradeToSsl cmd = new Commands.Debugging_UpgradeToSsl
            {
                m_flags = 0
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_UpgradeToSsl, Flags.c_NoCaching, cmd);

            if (reply != null)
            {

                if (reply.Payload is Commands.Debugging_UpgradeToSsl.Reply cmdReply)
                {
                    return cmdReply.m_success != 0;
                }
            }

            return false;

        }

        readonly Dictionary<int, uint[]> m_updateMissingPktTbl = new Dictionary<int, uint[]>();


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

            byte[] name = Encoding.UTF8.GetBytes(provider);

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

                if (reply.Payload is Commands.Debugging_MFUpdate_Start.Reply cmdReply)
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

                if (reply.Payload is Commands.Debugging_MFUpdate_AuthCommand.Reply cmdReply && cmdReply.m_success != 0)
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
            Commands.Debugging_MFUpdate_Authenticate cmd = new Commands.Debugging_MFUpdate_Authenticate
            {
                m_updateHandle = updateHandle
            };
            cmd.PrepareForSend(authenticationData);

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_MFUpdate_Authenticate, Flags.c_NoCaching, cmd);

            if (reply != null)
            {

                if (reply.Payload is Commands.Debugging_MFUpdate_Authenticate.Reply cmdReply && cmdReply.m_success != 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool UpdateGetMissingPackets(int updateHandle)
        {
            Commands.Debugging_MFUpdate_GetMissingPkts cmd = new Commands.Debugging_MFUpdate_GetMissingPkts
            {
                m_updateHandle = updateHandle
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_MFUpdate_GetMissingPkts, Flags.c_NoCaching, cmd);

            if (reply != null)
            {

                if (reply.Payload is Commands.Debugging_MFUpdate_GetMissingPkts.Reply cmdReply && cmdReply.m_success != 0)
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

            Commands.Debugging_MFUpdate_AddPacket cmd = new Commands.Debugging_MFUpdate_AddPacket
            {
                m_updateHandle = updateHandle,
                m_packetIndex = packetIndex,
                m_packetValidation = packetValidation
            };
            cmd.PrepareForSend(packetData);

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_MFUpdate_AddPacket, Flags.c_NoCaching, cmd);
            if (reply != null)
            {

                if (reply.Payload is Commands.Debugging_MFUpdate_AddPacket.Reply cmdReply)
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

            Commands.Debugging_MFUpdate_Install cmd = new Commands.Debugging_MFUpdate_Install
            {
                m_updateHandle = updateHandle
            };

            cmd.PrepareForSend(validationData);

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_MFUpdate_Install, Flags.c_NoCaching, cmd);

            if (reply != null)
            {

                if (reply.Payload is Commands.Debugging_MFUpdate_Install.Reply cmdReply)
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
                Commands.Debugging_Thread_CreateEx cmd = new Commands.Debugging_Thread_CreateEx
                {
                    m_md = methodIndex,
                    m_scratchPad = scratchPadLocation,
                    m_pid = pid
                };

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

                if (reply.Payload is Commands.Debugging_Thread_List.Reply cmdReply)
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

            return reply != null ? reply.Payload as Commands.Debugging_Thread_Stack.Reply : null;
        }

        public bool KillThread(uint pid)
        {
            Commands.Debugging_Thread_Kill cmd = new Commands.Debugging_Thread_Kill
            {
                m_pid = pid
            };

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
            Commands.Debugging_Thread_Suspend cmd = new Commands.Debugging_Thread_Suspend
            {
                m_pid = pid
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Thread_Suspend, 0, cmd);

            return reply != null && reply.IsPositiveAcknowledge();
        }

        public bool ResumeThread(uint pid)
        {
            Commands.Debugging_Thread_Resume cmd = new Commands.Debugging_Thread_Resume
            {
                m_pid = pid
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Thread_Resume, 0, cmd);

            return reply != null && reply.IsPositiveAcknowledge();
        }

        public RuntimeValue GetThreadException(uint pid)
        {
            Commands.Debugging_Thread_GetException cmd = new Commands.Debugging_Thread_GetException
            {
                m_pid = pid
            };

            return GetRuntimeValue(Commands.c_Debugging_Thread_GetException, cmd);
        }

        public RuntimeValue GetThread(uint pid)
        {
            Commands.Debugging_Thread_Get cmd = new Commands.Debugging_Thread_Get
            {
                m_pid = pid
            };

            return GetRuntimeValue(Commands.c_Debugging_Thread_Get, cmd);
        }

        public bool UnwindThread(uint pid, uint depth)
        {
            Commands.Debugging_Thread_Unwind cmd = new Commands.Debugging_Thread_Unwind
            {
                m_pid = pid,
                m_depth = depth
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Thread_Unwind, 0, cmd);

            return reply != null && reply.IsPositiveAcknowledge();
        }

        public bool SetIPOfStackFrame(uint pid, uint depth, uint IP, uint depthOfEvalStack)
        {
            Commands.Debugging_Stack_SetIP cmd = new Commands.Debugging_Stack_SetIP
            {
                m_pid = pid,
                m_depth = depth,

                m_IP = IP,
                m_depthOfEvalStack = depthOfEvalStack
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Stack_SetIP, 0, cmd);

            return reply != null && reply.IsPositiveAcknowledge();
        }

        public Commands.Debugging_Stack_Info.Reply GetStackInfo(uint pid, uint depth)
        {
            Commands.Debugging_Stack_Info cmd = new Commands.Debugging_Stack_Info
            {
                m_pid = pid,
                m_depth = depth
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Stack_Info, 0, cmd);

            return reply != null ? reply.Payload as Commands.Debugging_Stack_Info.Reply : null;
        }

        //--//

        public Commands.Debugging_TypeSys_AppDomains.Reply GetAppDomains()
        {
            if (!Capabilities.AppDomains)
                return null;

            Commands.Debugging_TypeSys_AppDomains cmd = new Commands.Debugging_TypeSys_AppDomains();

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_TypeSys_AppDomains, 0, cmd);

            return reply != null ? reply.Payload as Commands.Debugging_TypeSys_AppDomains.Reply : null;
        }

        public Commands.Debugging_TypeSys_Assemblies.Reply GetAssemblies()
        {
            Commands.Debugging_TypeSys_Assemblies cmd = new Commands.Debugging_TypeSys_Assemblies();

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_TypeSys_Assemblies, 0, cmd);

            return reply != null ? reply.Payload as Commands.Debugging_TypeSys_Assemblies.Reply : null;
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
            Commands.DebuggingResolveAssembly cmd = new Commands.DebuggingResolveAssembly
            {
                Idx = idx
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Resolve_Assembly, 0, cmd);

            return reply != null ? reply.Payload as Commands.DebuggingResolveAssembly.Reply : null;
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

            return reply == null ? ((uint NumOfArguments, uint NnumOfLocals, uint DepthOfEvalStack, bool Success))(0, 0, 0, false) : (reply.m_numOfArguments, reply.m_numOfLocals, reply.m_depthOfEvalStack, true);
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
            Commands.Debugging_Value_GetField cmd = new Commands.Debugging_Value_GetField
            {
                m_heapblock = (val == null ? 0 : val.m_handle.m_referenceID),
                m_offset = offset,
                m_fd = fd
            };

            return GetRuntimeValue(Commands.c_Debugging_Value_GetField, cmd);
        }

        public RuntimeValue GetStaticFieldValue(uint fd)
        {
            return GetFieldValue(null, 0, fd);
        }

        internal RuntimeValue AssignRuntimeValue(uint heapblockSrc, uint heapblockDst)
        {
            Commands.Debugging_Value_Assign cmd = new Commands.Debugging_Value_Assign
            {
                m_heapblockSrc = heapblockSrc,
                m_heapblockDst = heapblockDst
            };

            return GetRuntimeValue(Commands.c_Debugging_Value_Assign, cmd);
        }

        internal bool SetBlock(uint heapblock, uint dt, byte[] data)
        {
            Commands.Debugging_Value_SetBlock setBlock = new Commands.Debugging_Value_SetBlock
            {
                m_heapblock = heapblock,
                m_dt = dt
            };

            data.CopyTo(setBlock.m_value, 0);

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Value_SetBlock, 0, setBlock);

            return reply != null && reply.IsPositiveAcknowledge();
        }

        private OutgoingMessage CreateMessage_GetValue_Stack(uint pid, uint depth, StackValueKind kind, uint index)
        {
            Commands.Debugging_Value_GetStack cmd = new Commands.Debugging_Value_GetStack
            {
                m_pid = pid,
                m_depth = depth,
                m_kind = (uint)kind,
                m_index = index
            };

            return CreateMessage(Commands.c_Debugging_Value_GetStack, 0, cmd);
        }

        public bool ResizeScratchPad(int size)
        {
            Commands.Debugging_Value_ResizeScratchPad cmd = new Commands.Debugging_Value_ResizeScratchPad
            {
                m_size = size
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Value_ResizeScratchPad, 0, cmd);

            return reply != null && reply.IsPositiveAcknowledge();
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
                    if (message.Payload is Commands.Debugging_Value_Reply reply)
                    {
                        vals.Add(RuntimeValue.Convert(this, reply.m_values));
                    }
                }
            }

            return vals;
        }

        public RuntimeValue GetArrayElement(uint arrayReferenceId, uint index)
        {
            Commands.Debugging_Value_GetArray cmd = new Commands.Debugging_Value_GetArray
            {
                m_heapblock = arrayReferenceId,
                m_index = index
            };

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
            Commands.Debugging_Value_SetArray cmd = new Commands.Debugging_Value_SetArray
            {
                m_heapblock = heapblock,
                m_index = index
            };

            data.CopyTo(cmd.m_value, 0);

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Value_SetArray, 0, cmd);

            return reply != null && reply.IsPositiveAcknowledge();
        }

        public RuntimeValue GetScratchPadValue(int index)
        {
            Commands.Debugging_Value_GetScratchPad cmd = new Commands.Debugging_Value_GetScratchPad
            {
                m_index = index
            };

            return GetRuntimeValue(Commands.c_Debugging_Value_GetScratchPad, cmd);
        }

        public RuntimeValue AllocateObject(int scratchPadLocation, uint td)
        {
            Commands.Debugging_Value_AllocateObject cmd = new Commands.Debugging_Value_AllocateObject
            {
                m_index = scratchPadLocation,
                m_td = td
            };

            return GetRuntimeValue(Commands.c_Debugging_Value_AllocateObject, cmd);
        }

        public RuntimeValue AllocateString(int scratchPadLocation, string val)
        {
            Commands.Debugging_Value_AllocateString cmd = new Commands.Debugging_Value_AllocateString
            {
                m_index = scratchPadLocation,
                m_size = (uint)Encoding.UTF8.GetByteCount(val)
            };

            RuntimeValue rtv = GetRuntimeValue(Commands.c_Debugging_Value_AllocateString, cmd);

            if (rtv != null)
            {
                rtv.SetStringValue(val);
            }

            return rtv;
        }

        public RuntimeValue AllocateArray(int scratchPadLocation, uint td, int depth, int numOfElements)
        {
            Commands.Debugging_Value_AllocateArray cmd = new Commands.Debugging_Value_AllocateArray
            {
                m_index = scratchPadLocation,
                m_td = td,
                m_depth = (uint)depth,
                m_numOfElements = (uint)numOfElements
            };

            return GetRuntimeValue(Commands.c_Debugging_Value_AllocateArray, cmd);
        }

        public Commands.Debugging_Resolve_Type.Result ResolveType(uint td)
        {
            Commands.Debugging_Resolve_Type.Result result = (Commands.Debugging_Resolve_Type.Result)m_typeSysLookup.Lookup(TypeSysLookup.Type.Type, td);

            if (result == null)
            {
                Commands.Debugging_Resolve_Type cmd = new Commands.Debugging_Resolve_Type
                {
                    m_td = td
                };

                IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Resolve_Type, 0, cmd);

                if (reply != null)
                {

                    if (reply.Payload is Commands.Debugging_Resolve_Type.Reply cmdReply)
                    {
                        result = new Commands.Debugging_Resolve_Type.Result
                        {
                            m_name = Commands.GetZeroTerminatedString(cmdReply.m_type, false)
                        };

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
                Commands.Debugging_Resolve_Method cmd = new Commands.Debugging_Resolve_Method
                {
                    m_md = md
                };

                IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Resolve_Method, 0, cmd);

                if (reply != null)
                {

                    if (reply.Payload is Commands.Debugging_Resolve_Method.Reply cmdReply)
                    {
                        result = new Commands.Debugging_Resolve_Method.Result
                        {
                            m_name = Commands.GetZeroTerminatedString(cmdReply.m_method, false),
                            m_td = cmdReply.m_td
                        };

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
                Commands.Debugging_Resolve_Field cmd = new Commands.Debugging_Resolve_Field
                {
                    m_fd = fd
                };

                IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Resolve_Field, 0, cmd);
                if (reply != null)
                {

                    if (reply.Payload is Commands.Debugging_Resolve_Field.Reply cmdReply)
                    {
                        result = new Commands.Debugging_Resolve_Field.Result
                        {
                            m_name = Commands.GetZeroTerminatedString(cmdReply.m_name, false),
                            m_offset = cmdReply.m_offset,
                            m_td = cmdReply.m_td
                        };

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

            Commands.Debugging_Resolve_AppDomain cmd = new Commands.Debugging_Resolve_AppDomain
            {
                m_id = appDomainID
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Resolve_AppDomain, 0, cmd);

            return reply != null ? reply.Payload as Commands.Debugging_Resolve_AppDomain.Reply : null;
        }

        public string GetTypeName(uint td)
        {
            Commands.Debugging_Resolve_Type.Result resolvedType = ResolveType(td);

            return resolvedType?.m_name;
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

            return resolvedField != null ? (resolvedField.m_name, resolvedField.m_td, resolvedField.m_offset) : ((string Td, uint Offset, uint Success))(null, 0, 0);
        }

        public uint GetVirtualMethod(uint md, RuntimeValue obj)
        {
            Commands.Debugging_Resolve_VirtualMethod cmd = new Commands.Debugging_Resolve_VirtualMethod
            {
                m_md = md,
                m_obj = obj.ReferenceId
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Resolve_VirtualMethod, 0, cmd);

            if (reply != null)
            {

                if (reply.Payload is Commands.Debugging_Resolve_VirtualMethod.Reply cmdReply)
                {
                    return cmdReply.m_md;
                }
            }

            return 0;
        }

        public void InjectButtons(uint pressed, uint released)
        {
            Commands.Debugging_Button_Inject cmd = new Commands.Debugging_Button_Inject
            {
                m_pressed = pressed,
                m_released = released
            };

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
                        ThreadStatus ts = new ThreadStatus
                        {
                            m_pid = pids[i],
                            m_status = reply.m_status,
                            m_flags = reply.m_flags,
                            m_calls = new string[depth]
                        };

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

            return status != null ? (status.EntryPoint, status.StorageStart, status.StorageLength, true) : ((uint Entrypoint, uint StorageStart, uint StorageLength, bool Success))(0, 0, 0, false);
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
            Commands.Debugging_Info_SetJMC cmd = new Commands.Debugging_Info_SetJMC
            {
                m_fIsJMC = (uint)(fJMC ? 1 : 0),
                m_kind = (uint)kind,
                m_raw = index
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Debugging_Info_SetJMC, 0, cmd);

            return reply != null && reply.IsPositiveAcknowledge();
        }

        private bool DeploymentExecuteIncremental(
            List<byte[]> assemblies,
            bool skipErase = false,
            IProgress<MessageWithProgress> progress = null,
            IProgress<string> log = null)
        {
            // check if we do have the map
            if (FlashSectorMap.Count != 0)
            {
                // total size of assemblies to deploy 
                int deployLength = assemblies.Sum(a => a.Length);

                // build the deployment blob from the flash sector map
                // apply a filter so that we take only the blocks flag for deployment 
                var deploymentBlob = FlashSectorMap.Where(s => (s.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_DEPLOYMENT)
                    .Select(s => s.ToDeploymentSector())
                    .ToList();

                // rough check if there is enough room to deploy
                if(deploymentBlob.ToDeploymentBlockList().Sum(b => b.Size) < deployLength)
                {
                    // compose error message
                    string errorMessage = $"Deployment storage (available size: {deploymentBlob.ToDeploymentBlockList().Sum(b => b.Size)} bytes) is not large enough for assemblies to deploy (total size: {deployLength} bytes).";

                    log?.Report(errorMessage);
                    progress?.Report(new MessageWithProgress(""));
                    return false;
                }

                while (assemblies.Count > 0)
                {
                    //
                    // Only word-aligned assemblies are allowed.
                    //
                    if (assemblies.First().Length % 4 != 0)
                    {
                        log?.Report($"It's only possible to deploy word aligned assemblies. Failed to deploy assembly with {assemblies.First().Length} bytes.");
                        progress?.Report(new MessageWithProgress(""));
                        return false;
                    }

                    // setup counters
                    int remainingBytes = assemblies.First().Length;
                    int currentPosition = 0;

                    // find first block with available space
                    while (remainingBytes > 0)
                    {
                        // find the next sector with available space
                        var sector = deploymentBlob.FirstOrDefault(s => s.AvailableSpace > 0);

                        // check if there is available space
                        if (sector == null)
                        {
                            // couldn't find any more free blocks
                            break;
                        }
                        else
                        {
                            int bytesToCopy = Math.Min(sector.AvailableSpace, remainingBytes);

                            byte[] tempBuffer = new byte[bytesToCopy];

                            Array.Copy(assemblies.First(), tempBuffer, bytesToCopy);
                            sector.DeploymentData = tempBuffer;

                            remainingBytes -= bytesToCopy;
                            currentPosition += bytesToCopy;
                        }
                    }

                    if (remainingBytes == 0)
                    {
                        // assembly fully stored for deployment, remove it from the list
                        assemblies.RemoveAt(0);
                    }
                    else
                    {
                        // shouldn't happen, but couldn't find enough space to deploy all the assemblies!!
                        string errorMessage = $"Couldn't find a free deployment block to complete the deployment (remaining: {remainingBytes} bytes).";

                        log?.Report(errorMessage);
                        progress?.Report(new MessageWithProgress(""));
                        return false;
                    }
                }

                // get the block list to deploy (not empty)
                var blocksToDeploy = deploymentBlob.ToDeploymentBlockList().FindAll(b => b.DeploymentData.Length > 0);
                var deploymentSize = blocksToDeploy.Sum(b => b.DeploymentData.Length);
                var deployedBytes = 0;

                foreach (DeploymentBlock block in blocksToDeploy)
                {
                    // check if skip erase step was requested
                    if(!skipErase)
                    {
                        progress?.Report(new MessageWithProgress($"Erasing block @ 0x{block.StartAddress:X8}...", (uint)deployedBytes, (uint)deploymentSize));
                        log?.Report($"Erasing block @ 0x{block.StartAddress:X8}...");

                        // erase memory sector
                        (AccessMemoryErrorCodes ErrorCode, bool Success) eraseMemoryResult = EraseMemory(
                            (uint)block.StartAddress,
                            (uint)block.Size);

                        if (!eraseMemoryResult.Success)
                        {
                            log?.Report($"Error erasing device memory @ 0x{block.StartAddress:X8}.");

                            return false;
                        }
                        // check the error code returned
                        if(eraseMemoryResult.ErrorCode != AccessMemoryErrorCodes.NoError)
                        {
                            log?.Report($"Error erasing device memory @ 0x{block.StartAddress:X8}. Error code: {eraseMemoryResult.ErrorCode}.");
                            progress?.Report(new MessageWithProgress(""));
                            return false;
                        }
                    }

                    // block erased, write buffer
                    (AccessMemoryErrorCodes ErrorCode, bool Success) writeMemoryResult = WriteMemory(
                        (uint)block.StartAddress,
                        block.DeploymentData,
                        block.ProgramAligment,
                        deployedBytes,
                        deploymentSize,
                        progress,
                        log);

                    if (!writeMemoryResult.Success)
                    {
                        log?.Report($"Error writing to device memory @ 0x{block.StartAddress:X8} ({block.DeploymentData.Length} bytes).");
                        progress?.Report(new MessageWithProgress(""));
                        return false;
                    }
                    // check the error code returned
                    if (writeMemoryResult.ErrorCode != AccessMemoryErrorCodes.NoError)
                    {
                        log?.Report($"Error writing to device memory @ 0x{block.StartAddress:X8} ({block.DeploymentData.Length} bytes). Error code: {writeMemoryResult.ErrorCode}.");
                        progress?.Report(new MessageWithProgress(""));
                        return false;
                    }

#if DEBUG
                    if (System.Diagnostics.Debugger.IsAttached)
                    {
                        // check memory
                        var memCheck = CheckMemory((uint)block.StartAddress, block.DeploymentData);
                        Debug.Assert(memCheck, "Memory check Failed.");
                    }
#endif

                    deployedBytes += block.DeploymentData.Length;
                }

                // report progress
                progress?.Report(new MessageWithProgress(""));

                // deployment successful
                return true;
            }

            // invalid flash map
            progress?.Report(new MessageWithProgress(""));
            log?.Report("*** ERROR retrieving device flash map ***");

            return false;
        }

        private bool DeploymentExecuteFull(List<byte[]> assemblies, IProgress<string> progress)
        {
            uint storageStart;
            uint deployLength;
            byte[] closeHeader = new byte[8];

            // perform request
            var (Entrypoint, StorageStart, StorageLength, Success) = DeploymentGetStatusWithResult();

            // check if request was successfully executed
            if (!Success)
            {
                return false;
            }

            // fill in the local properties with the result
            storageStart = StorageStart;

            if (StorageLength == 0)
            {
                return false;
            }

            deployLength = (uint)closeHeader.Length;

            foreach (byte[] assembly in assemblies)
            {
                deployLength += (uint)assembly.Length;
            }

            progress?.Report(string.Format("Deploying assemblies for a total size of {0} bytes", deployLength));

            if (deployLength > StorageLength)
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
            return !!writeResult2.Success;
        }

        //public bool Deployment_Execute(ArrayList assemblies)
        //{
        //    return Deployment_Execute(assemblies, true, null);
        //}

        public bool DeploymentExecute(
            List<byte[]> assemblies,
            bool rebootAfterDeploy = true,
            bool skipErase = false,
            IProgress<MessageWithProgress> progress = null,
            IProgress<string> log = null)
        {
            bool deployedOK;

            var executionState = GetExecutionMode();

            if(executionState == Commands.DebuggingExecutionChangeConditions.State.Unknown)
            {
                log?.Report("*** ERROR: failed to get device execution state, aborting deployment ***");
                return false;
            }

            if (!executionState.IsDeviceInStoppedState())
            {
                if (!PauseExecution())
                {
                    log?.Report("*** ERROR: failed to pause execution in device, aborting deployment ***");
                    return false;
                }
            }

            if (Capabilities.IncrementalDeployment)
            {
                log?.Report("Incrementally deploying assemblies to the device");

                deployedOK = DeploymentExecuteIncremental(
                    assemblies,
                    skipErase,
                    progress,
                    log);
            }
            else
            {
                throw new NotSupportedException("Current version only supports incremental deployment. Check the image source code for Debugging_Execution_QueryCLRCapabilities. The capabilities list has to include c_CapabilityFlags_IncrementalDeployment.");
                //progress?.Report("Deploying assemblies to device");

                //fDeployedOK = await DeploymentExecuteFullAsync(assemblies, progress);
            }

            if (!deployedOK)
            {
                log?.Report("*** ERROR deploying assemblies to the device ***");
            }
            else
            {
                log?.Report("Assemblies successfully deployed to the device");

                if (rebootAfterDeploy)
                {

                    progress?.Report(new MessageWithProgress("Rebooting device..."));
                    log?.Report("Rebooting device...");

                    RebootDevice(RebootOptions.ClrOnly, log);
                }
            }

            return deployedOK;
        }

        public (uint Current, bool Success) SetProfilingMode(uint iSet, uint iReset)
        {
            Commands.Profiling_Command cmd = new Commands.Profiling_Command
            {
                m_command = Commands.Profiling_Command.c_Command_ChangeConditions,
                m_parm1 = iSet,
                m_parm2 = iReset
            };

            IncomingMessage reply = PerformSyncRequest(Commands.c_Profiling_Command, 0, cmd);
            if (reply != null)
            {

                return reply.Payload is Commands.Profiling_Command.Reply cmdReply ? (cmdReply.m_raw, true) : ((uint Current, bool Success))(0, true);
            }

            return (0, false);
        }

        public bool FlushProfilingStream()
        {
            Commands.Profiling_Command cmd = new Commands.Profiling_Command
            {
                m_command = Commands.Profiling_Command.c_Command_FlushStream
            };
            PerformSyncRequest(Commands.c_Profiling_Command, 0, cmd);
            return true;
        }

        private IncomingMessage DiscoverCLRCapability(uint capabilities)
        {
            Commands.Debugging_Execution_QueryCLRCapabilities cmd = new Commands.Debugging_Execution_QueryCLRCapabilities
            {
                m_caps = capabilities
            };

            return PerformSyncRequest(Commands.c_Debugging_Execution_QueryCLRCapabilities, Flags.c_NoCaching, cmd);
        }

        private uint DiscoverCLRCapabilityAsUint(uint capabilities)
        {
            uint ret = 0;

            IncomingMessage reply = DiscoverCLRCapability(capabilities);

            if (reply != null)
            {

                if (reply.Payload is Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply && cmdReply.m_data != null && cmdReply.m_data.Length == 4)
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
            Debug.WriteLine("==============================");
            Debug.WriteLine("DiscoverCLRCapability");

            return (CLRCapabilities.Capability)DiscoverCLRCapabilityAsUint(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityFlags);
        }

        private CLRCapabilities.SoftwareVersionProperties DiscoverSoftwareVersionProperties()
        {
            Debug.WriteLine("==============================");
            Debug.WriteLine("DiscoverSoftwareVersionProperties");

            IncomingMessage reply = DiscoverCLRCapability(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilitySoftwareVersion);

            Commands.Debugging_Execution_QueryCLRCapabilities.SoftwareVersion ver = new Commands.Debugging_Execution_QueryCLRCapabilities.SoftwareVersion();

            CLRCapabilities.SoftwareVersionProperties verCaps = new CLRCapabilities.SoftwareVersionProperties();

            if (reply != null)
            {

                if (reply.Payload is Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply && cmdReply.m_data != null)
                {
                    new Converter().Deserialize(ver, cmdReply.m_data);

                    verCaps = new CLRCapabilities.SoftwareVersionProperties(ver.BuildDate, ver.CompilerInfo, ver.CompilerVersion);
                }
            }

            return verCaps;
        }

        private CLRCapabilities.HalSystemInfoProperties DiscoverHalSystemInfoProperties()
        {
            Debug.WriteLine("==============================");
            Debug.WriteLine("DiscoverHalSystemInfoProperties");

            IncomingMessage reply = DiscoverCLRCapability(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityHalSystemInfo);

            Commands.Debugging_Execution_QueryCLRCapabilities.HalSystemInfo halSystemInfo = new Commands.Debugging_Execution_QueryCLRCapabilities.HalSystemInfo();

            CLRCapabilities.HalSystemInfoProperties halProps = new CLRCapabilities.HalSystemInfoProperties();

            if (reply != null)
            {

                if (reply.Payload is Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply && cmdReply.m_data != null)
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
            Debug.WriteLine("==============================");
            Debug.WriteLine("DiscoverClrInfoProperties");

            IncomingMessage reply = DiscoverCLRCapability(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityClrInfo);

            Commands.Debugging_Execution_QueryCLRCapabilities.ClrInfo clrInfo = new Commands.Debugging_Execution_QueryCLRCapabilities.ClrInfo();

            CLRCapabilities.ClrInfoProperties clrInfoProps = new CLRCapabilities.ClrInfoProperties();

            if (reply != null)
            {

                if (reply.Payload is Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply && cmdReply.m_data != null)
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
                if (reply.Payload is Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply && cmdReply.m_data != null)
                {

                    new Converter().Deserialize(targetInfo, cmdReply.m_data);

                    targetInfoProps = new CLRCapabilities.TargetInfoProperties(
                                                                        targetInfo.Version,
                                                                        targetInfo.Info,
                                                                        targetInfo.TargetName,
                                                                        targetInfo.PlatformName
                                                                        );
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
            const int maxRetryCount = 2;

            Commands.Debugging_Execution_QueryCLRCapabilities.NativeAssemblies nativeInteropAssemblies = new Commands.Debugging_Execution_QueryCLRCapabilities.NativeAssemblies();
            List<CLRCapabilities.NativeAssemblyProperties> nativeAssemblies = new List<CLRCapabilities.NativeAssemblyProperties>();

            // dev notes: we have this extra processing to keep this implementation backwards compatible with older targets

            // request assembly count
            IncomingMessage reply = DiscoverCLRCapability(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityInteropNativeAssembliesCount);

            if (reply != null)
            {
                if (reply.IsPositiveAcknowledge())
                {
                    uint assemblyCount = 0;
                    int retryCount = 0;
                    bool batchSizeAdjusted = false;

                    if (reply.Payload is Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply && cmdReply.m_data != null)
                    {
                        // check length 4 = size of int32_t type
                        if (cmdReply.m_data.Length == 4)
                        {
                            // can't use Converter because the deserialization of UInt32 is not supported
                            // replaced with a simple binary reader

                            MemoryStream stream = new MemoryStream(cmdReply.m_data);
                            BinaryReader reader = new BinaryReader(stream, Encoding.Unicode);

                            assemblyCount = reader.ReadUInt32();

                            // compute the assembly batch size that fits on target WP packet size
                            uint batchSize = WireProtocolPacketSize / Commands.Debugging_Execution_QueryCLRCapabilities.NativeAssemblyDetails.Size;
                            uint index = 0;

                            while (index < assemblyCount)
                            {
                                // get next batch

                                // need to pause between requests
                                Thread.Sleep(_InterRequestSleep);

                                // encode batch request
                                uint encodedRequest = batchSize << 24;
                                encodedRequest += index << 16;

                                // request assembly details
                                reply = DiscoverCLRCapability(
                                    Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityInteropNativeAssemblies +
                                    encodedRequest);

                                if (reply != null)
                                {
                                    if (reply.IsPositiveAcknowledge())
                                    {
                                        cmdReply = reply.Payload as Commands.Debugging_Execution_QueryCLRCapabilities.Reply;

                                        if (cmdReply != null && cmdReply.m_data != null)
                                        {
                                            nativeInteropAssemblies = new Commands.Debugging_Execution_QueryCLRCapabilities.NativeAssemblies();

                                            new Converter().Deserialize(nativeInteropAssemblies, cmdReply.m_data);

                                            nativeAssemblies.AddRange(
                                                   nativeInteropAssemblies.NativeInteropAssemblies.Select(
                                                       a => new CLRCapabilities.NativeAssemblyProperties(
                                                           a.Name,
                                                           a.CheckSum,
                                                           a.AssemblyVersion.Version)
                                                       )
                                                   );

                                            index += batchSize;

                                            // reset retry counter
                                            retryCount = 0;
                                        }
                                        else
                                        {
                                            if (retryCount < maxRetryCount)
                                            {
                                                retryCount++;
                                                // done here, try again
                                                continue;
                                            }

                                            return null;
                                        }
                                    }
                                    else
                                    {
                                        if (retryCount < maxRetryCount)
                                        {
                                            retryCount++;

                                            // done here, try again
                                            continue;
                                        }

                                        return null;
                                    }
                                }
                                else
                                {
                                    // check retry
                                    if (retryCount < maxRetryCount)
                                    {
                                        retryCount++;
                                    }
                                    else
                                    {
                                        // check if batch size has been adjusted
                                        if (!batchSizeAdjusted)
                                        {
                                            // adjust, if possible
                                            if (batchSize > 1)
                                            {
                                                // set to half the current size
                                                batchSize = (uint)Math.Floor(batchSize / 2.0);

                                                batchSizeAdjusted = true;

                                                // reset retry count
                                                retryCount = 0;

                                                // done here, try again
                                                continue;
                                            }
                                        }

                                        // nothing else to try, just quit
                                        return null;
                                    }
                                }
                            }

                            return nativeAssemblies;
                        }

                        return nativeInteropAssemblies.NativeInteropAssemblies.Select(a => new CLRCapabilities.NativeAssemblyProperties(a.Name, a.CheckSum, a.AssemblyVersion.Version)).ToList();
                    }
                }
                else
                {
                    // request failed, assuming that's because the target doesn't have support for this command, so going with the old style and get all assemblies at once

                    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
                    // it should be OK to remove this after a while as all targets should be brought up to date to support c_CapabilityInteropNativeAssembliesCount //
                    //////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

                    reply = DiscoverCLRCapability(Commands.Debugging_Execution_QueryCLRCapabilities.c_CapabilityInteropNativeAssemblies);

                    if (reply != null)
                    {
                        if (reply.IsPositiveAcknowledge())
                        {

                            if (reply.Payload is Commands.Debugging_Execution_QueryCLRCapabilities.Reply cmdReply && cmdReply.m_data != null)
                            {
                                new Converter().Deserialize(nativeInteropAssemblies, cmdReply.m_data);

                                return nativeInteropAssemblies.NativeInteropAssemblies.Select(a => new CLRCapabilities.NativeAssemblyProperties(a.Name, a.CheckSum, a.AssemblyVersion.Version)).ToList();
                            }
                        }
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

            if(clrFlags == 0)
            {
                return null;
            }
            Thread.Sleep(_InterRequestSleep);

            var softwareVersion = DiscoverSoftwareVersionProperties();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            if (softwareVersion.BuildDate == "")
            {
                return null;
            }
            Thread.Sleep(_InterRequestSleep);

            var halSysInfo = DiscoverHalSystemInfoProperties();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            if (halSysInfo.halVendorInfo == "")
            {
                return null;
            }
            Thread.Sleep(_InterRequestSleep);

            var clrInfo = DiscoverClrInfoProperties();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            if (clrInfo.clrVendorInfo == "")
            {
                return null;
            }
            Thread.Sleep(_InterRequestSleep);

            var solutionInfo = DiscoverTargetInfoProperties();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            if (solutionInfo.TargetName == "")
            {
                return null;
            }

            Thread.Sleep(_InterRequestSleep);


            var nativeAssembliesInfo = DiscoveryInteropNativeAssemblies();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            Thread.Sleep(_InterRequestSleep);

            // sanity check for NULL native assemblies
            if (nativeAssembliesInfo == null)
            {
                Debug.WriteLine("device reporting no native assemblies deployed");
                return null;
            }

            return new CLRCapabilities(clrFlags, softwareVersion, halSysInfo, clrInfo, solutionInfo, nativeAssembliesInfo);
        }

        public CLRCapabilities.TargetInfoProperties GetTargetInfo()
        {
            return DiscoverTargetInfoProperties();
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

            // get all wireless network AP configuration blocks
            var networkWirelessAPConfigs = GetAllWirelessAPConfigurations();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            // get all wireless network configuration blocks
            var x509Certificates = GetAllX509Certificates();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            // get all wireless network configuration blocks
            var x509DeviceCertificates = GetAllX509DeviceCertificates();
            // check for cancellation request
            if (cancellationToken.IsCancellationRequested)
            {
                // cancellation requested
                Debug.WriteLine("cancellation requested");
                return null;
            }

            return new DeviceConfiguration(
                networkConfigs, 
                networkWirelessConfigs, 
                networkWirelessAPConfigs,
                x509Certificates,
                x509DeviceCertificates);
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

                // was there an answer?
                if (networkConfig != null)
                {
                    // add to list, if valid
                    if (!networkConfig.IsUnknown)
                    {
                        networkConfigurations.Add(networkConfig);
                    }
                }
                else
                {
                    // no reply from device or no more config blocks available
                    break;
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

                // was there an answer?
                if (wirelessConfigProperties != null)
                {
                    // add to list, if valid
                    if (!wirelessConfigProperties.IsUnknown)
                    {
                        wireless80211Configurations.Add(wirelessConfigProperties);
                    }
                }
                else
                {
                    // no reply from device or no more config blocks available
                    break;
                }
            }
            while (!wirelessConfigProperties.IsUnknown);

            return wireless80211Configurations;
        }

        public List<DeviceConfiguration.WirelessAPConfigurationProperties> GetAllWirelessAPConfigurations()
        {
            List<DeviceConfiguration.WirelessAPConfigurationProperties> wirelessAPConfigurations = new List<DeviceConfiguration.WirelessAPConfigurationProperties>();

            DeviceConfiguration.WirelessAPConfigurationProperties wirelessAPConfigProperties = null;
            uint index = 0;

            do
            {
                // get next network configuration block, if available
                wirelessAPConfigProperties = GetWirelessAPConfiguratonProperties(index++);

                // was there an answer?
                if (wirelessAPConfigProperties != null)
                {
                    // add to list, if valid
                    if (!wirelessAPConfigProperties.IsUnknown)
                    {
                        wirelessAPConfigurations.Add(wirelessAPConfigProperties);
                    }
                }
                else
                {
                    // no reply from device or no more config blocks available
                    break;
                }
            }
            while (!wirelessAPConfigProperties.IsUnknown);

            return wirelessAPConfigurations;
        }

        public List<DeviceConfiguration.X509CaRootBundleProperties> GetAllX509Certificates()
        {
            List<DeviceConfiguration.X509CaRootBundleProperties> x509Certificates = new List<DeviceConfiguration.X509CaRootBundleProperties>();

            DeviceConfiguration.X509CaRootBundleProperties x509CertificatesProperties = null;
            uint index = 0;

            do
            {
                // get next X509 certificate configuration block, if available
                x509CertificatesProperties = GetX509CertificatesProperties(index++);

                // was there an answer?
                if (x509CertificatesProperties != null)
                {
                    // add to list, if valid
                    if (!x509CertificatesProperties.IsUnknown)
                    {
                        x509Certificates.Add(x509CertificatesProperties);
                    }
                }
                else
                {
                    // no reply from device or no more config blocks available
                    break;
                }
            }
            while (!x509CertificatesProperties.IsUnknown);

            return x509Certificates;
        }

        public List<DeviceConfiguration.X509DeviceCertificatesProperties> GetAllX509DeviceCertificates()
        {
            List<DeviceConfiguration.X509DeviceCertificatesProperties> x509DeviceCertificates = new List<DeviceConfiguration.X509DeviceCertificatesProperties>();

            DeviceConfiguration.X509DeviceCertificatesProperties x509DeviceCertificatesProperties = null;
            uint index = 0;

            do
            {
                // get next X509 certificate configuration block, if available
                x509DeviceCertificatesProperties = GetX509DeviceCertificatesProperties(index++);

                // was there an answer?
                if (x509DeviceCertificatesProperties != null)
                {
                    // add to list, if valid
                    if (!x509DeviceCertificatesProperties.IsUnknown)
                    {
                        x509DeviceCertificates.Add(x509DeviceCertificatesProperties);
                    }
                }
                else
                {
                    // no reply from device or no more config blocks available
                    break;
                }
            }
            while (!x509DeviceCertificatesProperties.IsUnknown);

            return x509DeviceCertificates;
        }

        public DeviceConfiguration.NetworkConfigurationProperties GetNetworkConfiguratonProperties(uint configurationBlockIndex)
        {
            Debug.WriteLine("NetworkConfiguratonProperties");

            IncomingMessage reply = GetDeviceConfiguration((uint)DeviceConfiguration.DeviceConfigurationOption.Network, configurationBlockIndex);

            Commands.Monitor_QueryConfiguration.NetworkConfiguration networkConfiguration = new Commands.Monitor_QueryConfiguration.NetworkConfiguration();

            DeviceConfiguration.NetworkConfigurationProperties networkConfigProperties = null;

            if (reply != null)
            {
                if (reply.IsPositiveAcknowledge())
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
            }

            return networkConfigProperties;
        }

        public DeviceConfiguration.Wireless80211ConfigurationProperties GetWireless80211ConfiguratonProperties(uint configurationBlockIndex)
        {
            Debug.WriteLine("NetworkWirelessConfiguratonProperties");

            IncomingMessage reply = GetDeviceConfiguration((uint)DeviceConfiguration.DeviceConfigurationOption.Wireless80211Network, configurationBlockIndex);

            Commands.Monitor_QueryConfiguration.NetworkWirelessConfiguration wirelessConfiguration = new Commands.Monitor_QueryConfiguration.NetworkWirelessConfiguration();

            DeviceConfiguration.Wireless80211ConfigurationProperties wirelessConfigProperties = null;

            if (reply != null)
            {
                if (reply.IsPositiveAcknowledge())
                {
                    if (reply.Payload is Commands.Monitor_QueryConfiguration.Reply cmdReply && cmdReply.Data != null)
                    {
                        new Converter().Deserialize(wirelessConfiguration, cmdReply.Data);

                        wirelessConfigProperties = new DeviceConfiguration.Wireless80211ConfigurationProperties(wirelessConfiguration);
                    }
                }
            }

            return wirelessConfigProperties;
        }

        public DeviceConfiguration.WirelessAPConfigurationProperties GetWirelessAPConfiguratonProperties(uint configurationBlockIndex)
        {
            Debug.WriteLine("NetworkWirelessAPConfiguratonProperties");

            IncomingMessage reply = GetDeviceConfiguration((uint)DeviceConfiguration.DeviceConfigurationOption.WirelessNetworkAP, configurationBlockIndex);

            Commands.Monitor_QueryConfiguration.NetworkWirelessAPConfiguration wirelessAPConfiguration = new Commands.Monitor_QueryConfiguration.NetworkWirelessAPConfiguration();

            DeviceConfiguration.WirelessAPConfigurationProperties wirelessAPConfigProperties = null;

            if (reply != null)
            {
                if (reply.IsPositiveAcknowledge())
                {
                    if (reply.Payload is Commands.Monitor_QueryConfiguration.Reply cmdReply && cmdReply.Data != null)
                    {
                        new Converter().Deserialize(wirelessAPConfiguration, cmdReply.Data);

                        wirelessAPConfigProperties = new DeviceConfiguration.WirelessAPConfigurationProperties(wirelessAPConfiguration);
                    }
                }
            }

            return wirelessAPConfigProperties;
        }

        public DeviceConfiguration.X509CaRootBundleProperties GetX509CertificatesProperties(uint configurationBlockIndex)
        {
            Debug.WriteLine("X509CertificateProperties");

            IncomingMessage reply = GetDeviceConfiguration((uint)DeviceConfiguration.DeviceConfigurationOption.X509Certificate, configurationBlockIndex);

            Commands.Monitor_QueryConfiguration.X509CaRootBundleConfig x509Certificate = new Commands.Monitor_QueryConfiguration.X509CaRootBundleConfig();

            DeviceConfiguration.X509CaRootBundleProperties x509CertificateProperties = null;

            if (reply != null)
            {
                if (reply.IsPositiveAcknowledge())
                {
                    if (reply.Payload is Commands.Monitor_QueryConfiguration.Reply cmdReply && cmdReply.Data != null)
                    {
                        new Converter().Deserialize(x509Certificate, cmdReply.Data);

                        x509CertificateProperties = new DeviceConfiguration.X509CaRootBundleProperties(x509Certificate);
                    }
                }
            }

            return x509CertificateProperties;
        }

        public DeviceConfiguration.X509DeviceCertificatesProperties GetX509DeviceCertificatesProperties(uint configurationBlockIndex)
        {
            Debug.WriteLine("X509DeviceCertificateProperties");

            IncomingMessage reply = GetDeviceConfiguration((uint)DeviceConfiguration.DeviceConfigurationOption.X509DeviceCertificates, configurationBlockIndex);

            Commands.Monitor_QueryConfiguration.X509DeviceCertificatesConfig x509DeviceCertificates = new Commands.Monitor_QueryConfiguration.X509DeviceCertificatesConfig();

            DeviceConfiguration.X509DeviceCertificatesProperties x509DeviceCertificateProperties = null;

            if (reply != null)
            {
                if (reply.IsPositiveAcknowledge())
                {
                    if (reply.Payload is Commands.Monitor_QueryConfiguration.Reply cmdReply && cmdReply.Data != null)
                    {
                        new Converter().Deserialize(x509DeviceCertificates, cmdReply.Data);

                        x509DeviceCertificateProperties = new DeviceConfiguration.X509DeviceCertificatesProperties(x509DeviceCertificates);
                    }
                }
            }

            return x509DeviceCertificateProperties;
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
            Commands.Monitor_FlashSectorMap.FlashSectorData configSector = new Commands.Monitor_FlashSectorMap.FlashSectorData();

            // the requirement to erase flash before storing is dependent on CLR capabilities which is only available if the device is running nanoCLR
            // when running nanoBooter those are not available
            // currently the only target that doesn't have nanoBooter is ESP32, so we are assuming that the remaining ones (being STM32 based) use internal flash for storing configuration blocks
            // if that is not the case, then the flash map won't show any config blocks and this step will be skipped 
            if ((IsConnectedTonanoCLR && Capabilities.ConfigBlockRequiresErase) ||
                IsConnectedTonanoBooter)
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
                configSector = FlashSectorMap.FirstOrDefault(item => (item.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CONFIG);

                // check if the device has a config sector
                if (configSector.NumBlocks > 0)
                {
                    // store the current configuration in case we need to revert this for some reason
                    var readConfigSector = ReadMemory(configSector.StartAddress, configSector.NumBlocks * configSector.BytesPerBlock);

                    if (readConfigSector.Success)
                    {
                        // start erasing the sector that holds the configuration block
                        var (ErrorCode, Success) = EraseMemory(configSector.StartAddress, 1);
                        if (Success)
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

                // counters to manage the chunked update process
                int count = configurationSerialized.Length;
                int position = 0;
                int attemptCount = 1;

                // flag to signal the update operation success/failure
                bool updateFailed = true;

                while (count > 0 &&
                        attemptCount >= 0)
                {
                    Commands.Monitor_UpdateConfiguration cmd = new Commands.Monitor_UpdateConfiguration
                    {
                        Configuration = (uint)DeviceConfiguration.DeviceConfigurationOption.All
                    };

                    // get packet length, either the maximum allowed size or whatever is still available to TX
                    int packetLength = Math.Min(GetPacketMaxLength(cmd), count);

                    // check if this is the last chunk
                    if(count <= packetLength &&
                       packetLength <= GetPacketMaxLength(cmd))
                    {
                        // yes, signal that by setting the Done field
                        cmd.Done = 1;
                    }
                    else
                    {
                        // no, more data is coming after this one
                        cmd.Done = 0;
                    }

                    cmd.PrepareForSend(configurationSerialized, packetLength, position);

                    IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_UpdateConfiguration, 0, cmd);

                    if (reply != null)
                    {
                        Commands.Monitor_UpdateConfiguration.Reply cmdReply = reply.Payload as Commands.Monitor_UpdateConfiguration.Reply;

                        if (!reply.IsPositiveAcknowledge() || cmdReply.ErrorCode != 0)
                        {
                            break;
                        }

                        count -= packetLength;
                        position += packetLength;

                        if(count == 0)
                        {
                            // update was OK, switch flag
                            updateFailed = false;
                        }

                        attemptCount = 1;
                    }
                    else
                    {
                        attemptCount--;
                    }
                }

                if(updateFailed)
                {
                    // failed to upload new configuration
                    // revert back old one

                    // TODO
                }
                else
                {
                    return true;
                }
            }

            // default to false
            return false;
        }

        public int GetPacketMaxLength(Commands.OverheadBase cmd)
        {
            return (int)WireProtocolPacketSize - cmd.Overhead;
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

            if(Capabilities.ConfigBlockRequiresErase)
            {
                // this device requires erasing the configuration block before updating it

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
                    else if (configuration.GetType().Equals(typeof(DeviceConfiguration.WirelessAPConfigurationProperties)))
                    {
                        // if list is empty and request index is 0
                        if (currentConfiguration.WirelessAPConfigurations.Count == 0 && blockIndex == 0)
                        {
                            currentConfiguration.WirelessAPConfigurations.Add(configuration as DeviceConfiguration.WirelessAPConfigurationProperties);
                        }
                        else
                        {
                            currentConfiguration.WirelessAPConfigurations[(int)blockIndex] = configuration as DeviceConfiguration.WirelessAPConfigurationProperties;
                        }
                    }
                    else if (configuration.GetType().Equals(typeof(DeviceConfiguration.X509CaRootBundleProperties)))
                    {
                        // if list is empty and request index is 0
                        if (currentConfiguration.X509Certificates.Count == 0 && blockIndex == 0)
                        {
                            currentConfiguration.X509Certificates.Add(configuration as DeviceConfiguration.X509CaRootBundleProperties);
                        }
                        else
                        {
                            currentConfiguration.X509Certificates[(int)blockIndex] = configuration as DeviceConfiguration.X509CaRootBundleProperties;
                        }
                    }
                    else if (configuration.GetType().Equals(typeof(DeviceConfiguration.X509DeviceCertificatesProperties)))
                    {
                        // if list is empty and request index is 0
                        if (currentConfiguration.X509DeviceCertificates.Count == 0 && blockIndex == 0)
                        {
                            currentConfiguration.X509DeviceCertificates.Add(configuration as DeviceConfiguration.X509DeviceCertificatesProperties);
                        }
                        else
                        {
                            currentConfiguration.X509DeviceCertificates[(int)blockIndex] = configuration as DeviceConfiguration.X509DeviceCertificatesProperties;
                        }
                    }

                    if (UpdateDeviceConfiguration(currentConfiguration))
                    {
                        // done here
                        return true;
                    }
                    else
                    {
                        // write failed, the old configuration it's supposed to have been reverted by now
                    }
                }
            }
            else
            {
                // no need to erase configuration block, just update what's required

                // serialize the configuration block
                var configurationSerialized = GetDeviceConfigurationSerialized(configuration);

                // counters to manage the chunked update process
                int count = configurationSerialized.Length;
                int position = 0;
                int attemptCount = 1;

                // flag to signal the update operation success/failure
                bool updateFailed = true;

                while ( count > 0 &&
                        attemptCount >= 0)
                {
                    Commands.Monitor_UpdateConfiguration cmd = new Commands.Monitor_UpdateConfiguration
                    {
                        Configuration = (uint)GetDeviceConfigurationOption(configuration)
                    };

                    // get packet length, either the maximum allowed size or whatever is still available to TX
                    int packetLength = Math.Min(GetPacketMaxLength(cmd), count);

                    // check if this is the last chunk
                    if (count <= packetLength &&
                        packetLength <= GetPacketMaxLength(cmd))
                    {
                        // yes, signal that by setting the Done field
                        cmd.Done = 1;
                    }
                    else
                    {
                        // no, more data is coming after this one
                        cmd.Done = 0;
                    }

                    cmd.PrepareForSend(configurationSerialized, packetLength, position);

                    IncomingMessage reply = PerformSyncRequest(Commands.c_Monitor_UpdateConfiguration, 0, cmd);

                    if (reply != null)
                    {
                        Commands.Monitor_UpdateConfiguration.Reply cmdReply = reply.Payload as Commands.Monitor_UpdateConfiguration.Reply;

                        if (!reply.IsPositiveAcknowledge() || cmdReply.ErrorCode != 0)
                        {
                            break;
                        }

                        count -= packetLength;
                        position += packetLength;

                        if (count == 0)
                        {
                            // update was OK, switch flag
                            updateFailed = false;
                        }

                        attemptCount = 1;
                    }
                    else
                    {
                        attemptCount--;
                    }
                }

                if (updateFailed)
                {
                    // failed to upload new configuration
                    // revert back old one

                    // TODO
                }
                else
                {
                    return true;
                }

            }

            // default to false
            return false;

        }

        private byte[] GetDeviceConfigurationSerialized<T>(T configuration)
        {   
            if (configuration.GetType().Equals(typeof(DeviceConfiguration.NetworkConfigurationProperties)))
            {
                var configBase = configuration as DeviceConfiguration.NetworkConfigurationProperties;
                return CreateConverter().Serialize((NetworkConfigurationBase)configBase);
            }
            else if (configuration.GetType().Equals(typeof(DeviceConfiguration.Wireless80211ConfigurationProperties)))
            {
                var configBase = configuration as DeviceConfiguration.Wireless80211ConfigurationProperties;
                return CreateConverter().Serialize((Wireless80211ConfigurationBase)configBase);
            }
            else if (configuration.GetType().Equals(typeof(DeviceConfiguration.WirelessAPConfigurationProperties)))
            {
                var configBase = configuration as DeviceConfiguration.WirelessAPConfigurationProperties;
                return CreateConverter().Serialize((WirelessAPConfigurationBase)configBase);
            }
            else if (configuration.GetType().Equals(typeof(DeviceConfiguration.X509CaRootBundleProperties)))
            {
                var configBase = configuration as DeviceConfiguration.X509CaRootBundleProperties;
                return CreateConverter().Serialize(configBase);
            }
            else if (configuration.GetType().Equals(typeof(DeviceConfiguration.X509DeviceCertificatesProperties)))
            {
                var configBase = configuration as DeviceConfiguration.X509DeviceCertificatesProperties;
                return CreateConverter().Serialize(configBase);
            }
            else
            {
                throw new NotSupportedException();
            }
        }

        private DeviceConfiguration.DeviceConfigurationOption GetDeviceConfigurationOption<T>(T configuration)
        {
            if (configuration.GetType().Equals(typeof(DeviceConfiguration.NetworkConfigurationProperties)))
            {
                return DeviceConfiguration.DeviceConfigurationOption.Network;
            }
            else if (configuration.GetType().Equals(typeof(DeviceConfiguration.Wireless80211ConfigurationProperties)))
            {
                return DeviceConfiguration.DeviceConfigurationOption.Wireless80211Network;
            }
            else if (configuration.GetType().Equals(typeof(DeviceConfiguration.WirelessAPConfigurationProperties)))
            {
                return DeviceConfiguration.DeviceConfigurationOption.WirelessNetworkAP;
            }
            else if (configuration.GetType().Equals(typeof(DeviceConfiguration.X509CaRootBundleProperties)))
            {
                return DeviceConfiguration.DeviceConfigurationOption.X509Certificate;
            }
            else if (configuration.GetType().Equals(typeof(DeviceConfiguration.X509DeviceCertificatesProperties)))
            {
                return DeviceConfiguration.DeviceConfigurationOption.X509DeviceCertificates;
            }
            else
            {
                throw new NotSupportedException();
            }
        }

#endregion

        private Thread CreateThreadHelper(ThreadStart ts)
        {
            Thread th = new Thread(ts);
            th.IsBackground = true;
            th.Start();

            return th;
        }
    }
}
