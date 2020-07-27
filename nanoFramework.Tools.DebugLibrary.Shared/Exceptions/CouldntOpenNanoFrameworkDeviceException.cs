//
// Copyright (c) 2017 The nanoFramework project contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger
{
    internal class CouldntOpenNanoFrameworkDeviceException : Exception
    {
        public CouldntOpenNanoFrameworkDeviceException()
        {
        }

        public CouldntOpenNanoFrameworkDeviceException(string message) : base(message)
        {
        }

        public CouldntOpenNanoFrameworkDeviceException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}