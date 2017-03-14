//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using NanoFramework.Tools.Debugger;

namespace NanoFramework.ANT.Services.NanoFrameworkService
{
    public class NFSerialDebugClientService : INFSerialDebugClientService
    {
        public PortBase SerialDebugClient { get; private set; }

        public NFSerialDebugClientService(PortBase client)
        {
            this.SerialDebugClient = client;
        }

    }
}
