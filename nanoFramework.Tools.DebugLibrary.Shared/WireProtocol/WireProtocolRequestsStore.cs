//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class WireProtocolRequestsStore
    {
        private readonly object _requestsLock = new object();
        private readonly Dictionary<Tuple<uint, ushort>, WireProtocolRequest> _requests = new();

        public void Add(WireProtocolRequest request)
        {
            lock (_requestsLock)
            {
                // it's wise to check if this key is already on the dictionary
                // can't use TryAdd because that's only available on the UWP API
                Tuple<uint, ushort> newKey = GetKey(request);

                if (_requests.ContainsKey(newKey))
                {
                    // remove the last one, before adding this
                    _ = _requests.Remove(newKey);
                }

                _requests.Add(newKey, request);
            }
        }

        public bool Remove(Packet header)
        {
            lock (_requestsLock)
            {
                return _requests.Remove(new Tuple<uint, ushort>(header.Cmd, header.Seq));
            }
        }

        public bool Remove(WireProtocolRequest request)
        {
            lock (_requestsLock)
            {
                if(_requests.Remove(GetKey(request)))
                {
                    request.RequestAborted();

                    return true;
                }
                else
                {
                    return false;
                }
            }
        }

        public int GetCount()
        {
            lock (_requestsLock)
            {
                return _requests.Count;
            }
        }

        public WireProtocolRequest GetByReplyHeader(Packet header)
        {
            lock (_requestsLock)
            {
                return _requests.TryGetValue(new Tuple<uint, ushort>(header.Cmd, header.SeqReply), out WireProtocolRequest requestState) ? requestState : null;
            }
        }

        private Tuple<uint, ushort> GetKey(WireProtocolRequest request)
        {
            return new Tuple<uint, ushort>(request.OutgoingMessage.Header.Cmd, request.OutgoingMessage.Header.Seq);
        }
    }
}
