// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;

namespace nanoFramework.Tools.Debugger
{
    public interface IPort
    {
        /// <summary>
        /// Gets the Instance ID of the port that is unique among all ports
        /// (regardless of the type of port).
        /// </summary>
        string InstanceId { get; }

        int AvailableBytes { get; }

        int SendBuffer(byte[] buffer);

        byte[] ReadBuffer(int bytesToRead);

        ConnectPortResult ConnectDevice();

        void DisconnectDevice(bool force = false);
    }
}
