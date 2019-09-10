//
// Copyright (c) 2019 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger
{
    public abstract partial class PortMessageBase
    {
        /// <summary>
        /// Event that is raised when a log message is available.
        /// </summary>
        public event EventHandler<StringEventArgs> LogMessageAvailable;

        public void OnLogMessageAvailable(string message)
        {
            LogMessageAvailable?.Invoke(this, new StringEventArgs(message));
        }

    }
}
