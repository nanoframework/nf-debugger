//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger
{
    public partial class Engine
    {
        #region RPC Support

        private async Task RpcReceiveQueryAsync(IncomingMessage message, Commands.Debugging_Messaging_Query query)
        {
            Commands.Debugging_Messaging_Address addr = query.m_addr;
            // FIXME
            //EndPointRegistration eep = RpcFind(addr.m_to_Type, addr.m_to_Id, true);

            Commands.Debugging_Messaging_Query.Reply reply = new Commands.Debugging_Messaging_Query.Reply();

            // FIXME
            //reply.m_found = (eep != null) ? 1u : 0u;
            reply.m_addr = addr;

            await message.ReplyAsync(CreateConverter(), Flags.c_NonCritical, reply, new CancellationToken());
        }

        #endregion
    }
}
