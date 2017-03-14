//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace NanoFramework.Tools.Debugger
{
    public class StringEventArgs : EventArgs
    {
        public StringEventArgs(string eventArgs)
        {
            EventText = eventArgs;
        }

        public string EventText { get;  private set; } = null;
    }
}
