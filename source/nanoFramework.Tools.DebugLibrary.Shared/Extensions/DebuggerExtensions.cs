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
        /// Check if device state is <see cref=""/>.
        /// </summary>
        /// <param name="debugEngine"></param>
        /// <returns></returns>
        public static async Task<bool> IsDeviceInInitializeStateAsync(this Engine debugEngine)
        {
            var result = await debugEngine.GetExecutionModeAsync();

            if (result != WireProtocol.Commands.DebuggingExecutionChangeConditions.State.Unknown)
            {
                return (result == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.Initialize);
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
            var result = await debugEngine.GetExecutionModeAsync();

            if (result != WireProtocol.Commands.DebuggingExecutionChangeConditions.State.Unknown)
            {
                return (result == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramExited);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Check if device execution state is: program running.
        /// </summary>
        /// <param name="debugEngine"></param>
        /// <returns></returns>
        public static async Task<bool> IsDeviceInProgramRunningStateAsync(this Engine debugEngine)
        {
            var result = await debugEngine.GetExecutionModeAsync();

            if (result != WireProtocol.Commands.DebuggingExecutionChangeConditions.State.Unknown)
            {
                return (result == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramRunning);
            }
            else
            {
                return false;
            }
        }

    }
}
