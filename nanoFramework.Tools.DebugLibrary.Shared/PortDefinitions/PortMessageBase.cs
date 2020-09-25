//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger
{
    public abstract class PortMessageBase
    {
        /// <summary>
        /// Event that is raised when a log message is available.
        /// </summary>
        public event EventHandler<StringEventArgs> LogMessageAvailable;

        protected virtual void OnLogMessageAvailable(string message)
        {
            LogMessageAvailable?.Invoke(this, new StringEventArgs(message));
        }
    }
}
