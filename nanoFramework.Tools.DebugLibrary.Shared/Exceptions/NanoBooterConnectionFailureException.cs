//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger
{
    internal class NanoBooterConnectionFailureException : Exception
    {
        public NanoBooterConnectionFailureException()
        {
        }

        public NanoBooterConnectionFailureException(string message) : base(message)
        {
        }

        public NanoBooterConnectionFailureException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
