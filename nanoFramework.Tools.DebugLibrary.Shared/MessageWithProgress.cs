//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger
{
    public class MessageWithProgress : IProgress<MessageWithProgress>
    {
        private readonly Action<string, uint, uint> startMessageWithProgress;

        public MessageWithProgress(Action<string, uint, uint> startMessageWithProgress)
        {
            this.startMessageWithProgress = startMessageWithProgress;
        }

        public MessageWithProgress(string message, uint current = 100, uint total = 100)
        {
            Current = current;
            Total = total;
            Message = message;
        }


        public uint Current { get; private set; }
        public uint Total { get; private set; }
        public string Message { get; private set; }


        public void Report(MessageWithProgress value)
        {
            startMessageWithProgress(Message, Current, Total);
        }
    }
}