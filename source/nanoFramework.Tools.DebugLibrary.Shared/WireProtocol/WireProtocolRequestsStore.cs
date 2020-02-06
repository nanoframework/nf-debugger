using System;
using System.Collections.Generic;
using System.Linq;

namespace nanoFramework.Tools.Debugger.WireProtocol
{
    public class WireProtocolRequestsStore
    {
        private readonly object _requestsLock = new object();
        private readonly Dictionary<Tuple<uint, ushort>, WireProtocolRequest> _requests = new Dictionary<Tuple<uint, ushort>, WireProtocolRequest>();

        public void Add(WireProtocolRequest request)
        {
            lock (_requestsLock)
            {
                // it's wise to check if this key is already on the dictionary
                // can't use TryAdd because that's only available on the UWP API
                var newKey = new Tuple<uint, ushort>(request.OutgoingMessage.Header.Cmd, request.OutgoingMessage.Header.Seq);
                if (_requests.ContainsKey(newKey))
                {
                    // remove the last one, before adding this
                    _requests.Remove(newKey);
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

        public ICollection<WireProtocolRequest> FindAllToCancel()
        {
            lock (_requestsLock)
            {
                return _requests.Values.Where(x => x.Expires < DateTime.UtcNow || x.CancellationToken.IsCancellationRequested).ToList();
            }
        }
    }
}
