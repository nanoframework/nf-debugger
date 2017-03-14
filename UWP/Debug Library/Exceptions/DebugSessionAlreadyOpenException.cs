//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace NanoFramework.Tools.Debugger
{
    internal class DebugSessionAlreadyOpenException : Exception
    {
        public DebugSessionAlreadyOpenException()
        {
        }

        public DebugSessionAlreadyOpenException(string message) : base(message)
        {
        }

        public DebugSessionAlreadyOpenException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}