﻿//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using System;

namespace nanoFramework.Tools.Debugger
{
    internal class DeviceNotConnectedException : Exception
    {
        public DeviceNotConnectedException()
        {
        }

        public DeviceNotConnectedException(string message) : base(message)
        {
        }

        public DeviceNotConnectedException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}