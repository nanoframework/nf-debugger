//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the Microsoft .NET Micro Framework and is unsupported. 
// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// 
// Licensed under the Apache License, Version 2.0 (the "License"); you may not use these files except in compliance with the License.
// You may obtain a copy of the License at:
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the License for the specific language governing
// permissions and limitations under the License.
// 
//////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

using Microsoft.SPOT.Debugger.WireProtocol;

namespace Microsoft.SPOT.Debugger
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
