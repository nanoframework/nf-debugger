//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.Extensions
{
    public static class DebuggerExtensions
    {
        /// <summary>
        /// Check if device is in initialized state.
        /// </summary>
        /// <param name="debugEngine"></param>
        /// <returns></returns>
        public static async ValueTask<bool> IsDeviceInInitializeStateAsync(this Engine debugEngine)
        {
            var result = await debugEngine.SetExecutionModeAsync(0, 0);

            if (result.success)
            {
                return ((result.currentExecutionMode & WireProtocol.Commands.Debugging_Execution_ChangeConditions.c_State_Mask) == WireProtocol.Commands.Debugging_Execution_ChangeConditions.c_State_Initialize);
            }
            else
            {
                return false;
            }
        }
    }
}
