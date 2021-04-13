//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;

namespace nanoFramework.Tools.Debugger
{
    public interface IPort
    {
        int SendBuffer(byte[] buffer, TimeSpan waiTimeout);

        byte[] ReadBuffer(int bytesToRead, TimeSpan waiTimeout);

        bool ConnectDevice();

        void DisconnectDevice();
    }
}
