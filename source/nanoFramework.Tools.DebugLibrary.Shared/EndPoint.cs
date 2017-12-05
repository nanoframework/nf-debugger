//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.Reflection;
//using System.Runtime.Remoting;
//using System.Runtime.Remoting.Messaging;
//using System.Runtime.Remoting.Proxies;
//using System.Runtime.Serialization.Formatters.Binary;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger
{
    public class EndPoint
    {
        internal Engine _engine;

        internal uint _type;
        internal uint _id;

        internal int _sequence;

        private object _server;
        private Type _serverClassToRemote;

        internal EndPoint(Type type, uint id, Engine engine)
        {
            _type = BinaryFormatter.LookupHash(type);
            _id = id;
            _sequence = 0;
            _engine = engine;
        }

        public EndPoint(Type type, uint id, object server, Type classToRemote, Engine engine)
            : this(type, id, engine)
        {
            _server = server;
            _serverClassToRemote = classToRemote;
        }

        public void Register()
        {
            _engine.RpcRegisterEndPoint(this);
        }

        public void Deregister()
        {
            _engine.RpcDeregisterEndPoint(this);
        }

        internal bool CheckDestination(EndPoint ep)
        {
            return _engine.RpcCheck(InitializeAddressForTransmission(ep));
        }

        internal bool IsRpcServer
        {
            get { return _server != null; }
        }

        private Commands.Debugging_Messaging_Address InitializeAddressForTransmission(EndPoint epTo)
        {
            Commands.Debugging_Messaging_Address addr = new Commands.Debugging_Messaging_Address();

            addr.m_seq = (uint)Interlocked.Increment(ref _sequence);

            addr.m_from_Type = _type;
            addr.m_from_Id = _id;

            addr.m_to_Type = epTo._type;
            addr.m_to_Id = epTo._id;

            return addr;
        }

        internal Commands.Debugging_Messaging_Address InitializeAddressForReception()
        {
            Commands.Debugging_Messaging_Address addr = new Commands.Debugging_Messaging_Address();

            addr.m_seq = 0;

            addr.m_from_Type = 0;
            addr.m_from_Id = 0;

            addr.m_to_Type = _type;
            addr.m_to_Id = _id;

            return addr;
        }

        internal object SendMessage(EndPoint ep, int timeout, MessageCall call)
        {
            object data = call.CreateMessagePayload();

            byte[] payload = _engine.CreateBinaryFormatter().Serialize(data);

            byte[] res = SendMessageInner(ep, timeout, payload);

            if (res == null)
            {
                throw new Exception(string.Format("Remote call '{0}' failed", call.Name));
            }

            object o = _engine.CreateBinaryFormatter().Deserialize(res);

            RemotedException ex = o as RemotedException;

            if (ex != null)
            {
                ex.Raise();
            }

            return o;
        }

        internal void DispatchMessage(Message message)
        {
            object res = null;

            try
            {
                MessageCall call = MessageCall.CreateFromMessagePayload(message.Payload);

                object[] args = call.Args;
                Type[] argTypes = new Type[(args == null) ? 0 : args.Length];

                if (args != null)
                {
                    for (int i = args.Length - 1; i >= 0; i--)
                    {
                        object arg = args[i];

                        argTypes[i] = (arg == null) ? typeof(object) : arg.GetType();
                    }
                }

                MethodInfo mi = _serverClassToRemote.GetMethod(call.Name, argTypes);

                if (mi == null) throw new Exception(string.Format("Could not find remote method '{0}'", call.Name));

                res = mi.Invoke(_server, call.Args);
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null)
                {
                    //If an exception is thrown in the target method, it will be packaged up as the InnerException
                    ex = ex.InnerException;
                }

                res = new RemotedException(ex);
            }

            try
            {
                message.Reply(res);
            }
            catch
            {
            }
        }

        internal byte[] SendMessageInner(EndPoint ep, int timeout, byte[] data)
        {
            return _engine.RpcSend(InitializeAddressForTransmission(ep), timeout, data);
        }

        internal void ReplyInner(Message msg, byte[] data)
        {
            _engine.RpcReply(msg.m_addr, data);
        }

        //static public object GetObject(Engine eng, Type type, uint id, Type classToRemote)
        //{
        //    return GetObject(eng, new EndPoint(type, id, eng), classToRemote);
        //}

        //static internal object GetObject(Engine eng, EndPoint ep, Type classToRemote)
        //{
        //    uint id = eng.RpcGetUniqueEndpointId();

        //    EndPoint epLocal = new EndPoint(typeof(EndPointProxy), id, eng);

        //    EndPointProxy prx = new EndPointProxy(eng, epLocal, ep, classToRemote);

        //    return prx.GetTransparentProxy();
        //}

        //internal class EndPointProxy : RealProxy, IDisposable
        //{
        //    private Engine _engine;
        //    private Type _type;
        //    private EndPoint _from;
        //    private EndPoint _to;

        //    internal EndPointProxy(Engine engine, EndPoint from, EndPoint to, Type type)
        //        : base(type)
        //    {
        //        from.Register();

        //        // FIXME can't use async in a constructor
        //        //if (await from.CheckDestinationAsync(to) == false)
        //        //{
        //        //    from.Deregister();

        //        //    throw new ArgumentException("Cannot connect to device EndPoint");
        //        //}

        //        _engine = engine;
        //        _from = from;
        //        _to = to;
        //        _type = type;
        //    }

        //    ~EndPointProxy()
        //    {
        //        Dispose();
        //    }

        //    public void Dispose()
        //    {
        //        try
        //        {
        //            if (_from != null)
        //            {
        //                _from.Deregister();
        //            }
        //        }
        //        catch
        //        {
        //        }
        //        finally
        //        {
        //            _engine = null;
        //            _from = null;
        //            _to = null;
        //            _type = null;
        //        }
        //    }

        //    public override IMessage Invoke(IMessage message)
        //    {
        //        IMethodMessage myMethodMessage = (IMethodMessage)message;

        //        if (myMethodMessage.MethodSignature is Array)
        //        {
        //            foreach (Type t in (Array)myMethodMessage.MethodSignature)
        //            {
        //                if (t.IsByRef)
        //                {
        //                    throw new NotSupportedException("ByRef parameters are not supported");
        //                }
        //            }
        //        }

        //        MethodInfo mi = myMethodMessage.MethodBase as MethodInfo;

        //        if (mi != null)
        //        {
        //            BinaryFormatter.PopulateFromType(mi.ReturnType);
        //        }

        //        MessageCall call = MessageCall.CreateFromIMethodMessage(myMethodMessage);

        //        object returnValue = _from.SendMessageAsync(_to, 60 * 1000, call);

        //        // Build the return message to pass back to the transparent proxy.
        //        return new ReturnMessage(returnValue, null, 0, null, (IMethodCallMessage)message);
        //    }
        //}
    }
}
