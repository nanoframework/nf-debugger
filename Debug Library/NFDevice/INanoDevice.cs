//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger
{
    public interface INanoDevice
    {
        Task<bool> ConnectAsync();

        void Disconnect();
    }
}
