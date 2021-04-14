//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger
{
    public interface IPort
    {
        int SendBuffer(byte[] buffer);

        byte[] ReadBuffer(int bytesToRead);

        bool ConnectDevice();

        void DisconnectDevice();
    }
}
