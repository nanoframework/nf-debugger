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
                _requests.Add(new Tuple<uint, ushort>(request.OutgoingMessage.Header.Cmd, request.OutgoingMessage.Header.Seq), request);
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
