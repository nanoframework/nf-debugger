//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.Debugger.Extensions
{
    public static class IncomingMessageExtensions
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="message"></param>
        /// <returns></returns>
        public static bool IsPositiveAcknowledge(this IncomingMessage message)
        {
            return message != null && ((message.Header.Flags & Flags.c_ACK) != 0);
        }
    }
}
