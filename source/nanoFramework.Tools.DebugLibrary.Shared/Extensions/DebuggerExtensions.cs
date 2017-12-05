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
        public static bool IsDeviceInInitializeState(this Engine debugEngine)
        {
            var result = debugEngine.GetExecutionMode();

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
        public static bool IsDeviceInExitedState(this Engine debugEngine)
        {
            var result = debugEngine.GetExecutionMode();

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
        public static bool IsDeviceInProgramRunningState(this Engine debugEngine)
        {
            var result = debugEngine.GetExecutionMode();

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
