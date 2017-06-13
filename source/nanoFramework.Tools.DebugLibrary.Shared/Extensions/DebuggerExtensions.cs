//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

namespace nanoFramework.Tools.Debugger.Extensions
{
    public static class DebuggerExtensions
    {
        /// <summary>
        /// Check if device is in initialized state.
        /// </summary>
        /// <param name="debugEngine"></param>
        /// <returns></returns>
        public static async System.Threading.Tasks.Task<bool> IsDeviceInInitializeStateAsync(this Engine debugEngine)
        {
            try
            {
                var result = await debugEngine.SetExecutionModeAsync(0, 0);
                if (result.Item2)
                {
                    return ((result.Item1 & WireProtocol.Commands.Debugging_Execution_ChangeConditions.c_State_Mask) == WireProtocol.Commands.Debugging_Execution_ChangeConditions.c_State_Initialize);
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
