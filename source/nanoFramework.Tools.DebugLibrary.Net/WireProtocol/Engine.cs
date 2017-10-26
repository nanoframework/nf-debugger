//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger
{
    public partial class Engine
    {
        public BinaryFormatter CreateBinaryFormatter()
        {
            return new BinaryFormatter(Capabilities);
        }

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
                IControllerRemote remote = m_ctrl as IControllerRemote;

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

                IControllerRemote remote = m_ctrl as IControllerRemote;
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

        private async Task RpcReceiveQueryAsync(IncomingMessage message, Commands.Debugging_Messaging_Query query)
        {
            Commands.Debugging_Messaging_Address addr = query.m_addr;
            EndPointRegistration eep = RpcFind(addr.m_to_Type, addr.m_to_Id, true);

            Commands.Debugging_Messaging_Query.Reply reply = new Commands.Debugging_Messaging_Query.Reply();

            reply.m_found = (eep != null) ? 1u : 0u;
            reply.m_addr = addr;

            await message.ReplyAsync(CreateConverter(), Flags.c_NonCritical, reply, new CancellationToken());
        }

        internal async Task<bool> RpcCheckAsync(Commands.Debugging_Messaging_Address addr)
        {
            Commands.Debugging_Messaging_Query cmd = new Commands.Debugging_Messaging_Query();

            cmd.m_addr = addr;

            IncomingMessage reply = await SyncMessageAsync(Commands.c_Debugging_Messaging_Query, 0, cmd);
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

        internal async Task<byte[]> RpcSendAsync(Commands.Debugging_Messaging_Address addr, int timeout, byte[] data)
        {
            EndPointRegistration.OutboundRequest or = null;
            byte[] res = null;

            try
            {
                or = await RpcSend_SetupAsync(addr, data);
                if (or != null)
                {
                    or.WaitHandle.WaitOne(timeout, false);

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

        private async Task<EndPointRegistration.OutboundRequest> RpcSend_SetupAsync(Commands.Debugging_Messaging_Address addr, byte[] data)
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

                IncomingMessage reply = await SyncMessageAsync(Commands.c_Debugging_Messaging_Send, 0, cmd);
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

        internal async Task<bool> RpcReplyAsync(Commands.Debugging_Messaging_Address addr, byte[] data)
        {
            Commands.Debugging_Messaging_Reply cmd = new Commands.Debugging_Messaging_Reply();

            cmd.m_addr = addr;
            cmd.m_data = data;

            IncomingMessage reply = await SyncMessageAsync(Commands.c_Debugging_Messaging_Reply, 0, cmd);
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

        internal uint RpcGetUniqueEndpointId()
        {
            return m_ctrl.GetUniqueEndpointId();
        }

        #endregion

        internal async Task<Request> RequestAsync(OutgoingMessage message, int retries, int timeout)
        {
            Request req = new Request(m_ctrl, message, retries, timeout, null);

            // FIXME
            //lock (m_state.SyncObject)
            //{

            //    //Checking whether IsRunning and adding the request to m_requests
            //    //needs to be atomic to avoid adding a request after the Engine
            //    //has been stopped.

            //    if (!IsRunning)
            //    {
            //        throw new ApplicationException("Engine is not running or process has exited.");
            //    }

            //    m_requests.Add(req);

            await req.SendAsync(new CancellationToken());
            //}

            return req;
        }

        /// <summary>
        /// Global lock object for synchornizing message request. This ensures there is only one
        /// outstanding request at any point of time. 
        /// </summary>
        internal object m_ReqSyncLock = new object();

        private Task<Request> AsyncMessage(uint command, uint flags, object payload, int retries, int timeout)
        {
            OutgoingMessage msg = CreateMessage(command, flags, payload);

            return RequestAsync(msg, retries, timeout);
        }

        private async Task<IncomingMessage> MessageAsync(uint command, uint flags, object payload, int retries, int timeout)
        {
            // FIXME
            // Lock on m_ReqSyncLock object, so only one thread is active inside the block.
            //lock (m_ReqSyncLock)
            //{
            Request req = await AsyncMessage(command, flags, payload, retries, timeout);

            return await req.WaitAsync();
            //}
        }

        private async Task<IncomingMessage> SyncMessageAsync(uint command, uint flags, object payload)
        {
            return await MessageAsync(command, flags, payload, RETRIES_DEFAULT, TIMEOUT_DEFAULT);
        }
    }
}
