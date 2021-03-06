﻿//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.Debugger
{
    public class ThreadStatus
    {
        public const uint STATUS_Ready = Commands.Debugging_Thread_Stack.Reply.TH_S_Ready;
        public const uint STATUS_Waiting = Commands.Debugging_Thread_Stack.Reply.TH_S_Waiting;
        public const uint STATUS_Terminated = Commands.Debugging_Thread_Stack.Reply.TH_S_Terminated;

        public const uint FLAGS_Suspended = Commands.Debugging_Thread_Stack.Reply.TH_F_Suspended;

        public uint m_pid;
        public uint m_flags;
        public uint m_status;
        public string[] m_calls;
    }
}
