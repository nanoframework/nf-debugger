//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace NanoFramework.Tools.Debugger
{
    internal class NanoUserExitException : Exception
    {
        public NanoUserExitException()
        {
        }

        public NanoUserExitException(string message) : base(message)
        {
        }

        public NanoUserExitException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
