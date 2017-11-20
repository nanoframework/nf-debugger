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
        public static async Task<bool> IsDeviceInInitializeStateAsync(this Engine debugEngine)
        {
            var result = await debugEngine.SetExecutionModeAsync(0, 0);

            if (result.success)
            {
                var currentState = (result.currentExecutionMode & WireProtocol.Commands.Debugging_Execution_ChangeConditions.c_State_Mask);
                return (currentState == WireProtocol.Commands.Debugging_Execution_ChangeConditions.c_State_Initialize);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Check if device execution state is: program exited.
        /// </summary>
        /// <param name="debugEngine"></param>
        /// <returns></returns>
        public static async Task<bool> IsDeviceInExitedStateAsync(this Engine debugEngine)
        {
            var result = await debugEngine.SetExecutionModeAsync(0, 0);

            if (result.success)
            {
                var currentState = (result.currentExecutionMode & WireProtocol.Commands.Debugging_Execution_ChangeConditions.c_State_Mask);
                return (currentState == WireProtocol.Commands.Debugging_Execution_ChangeConditions.c_State_ProgramExited);
            }
            else
            {
                return false;
            }
        }
    }
}
