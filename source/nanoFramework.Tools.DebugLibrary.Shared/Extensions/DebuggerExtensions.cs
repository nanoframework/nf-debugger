//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System.Diagnostics;

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
                // engine is in initialised state if it's not running a program or if the program execution is stopped (after having running one)
                var filteredResult = result & (WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramExited | WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramRunning);

                Debug.WriteLine($"Device state is: {filteredResult.OutputDeviceExecutionState()}.");

                return (filteredResult == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.Initialize);
            }
            else
            {
                Debug.WriteLine("Couldn't get device execution mode.");

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
                var filteredResult = result & WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramExited;

                Debug.WriteLine($"Device state is: {filteredResult.OutputDeviceExecutionState()}.");

                return (filteredResult == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramExited);
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
                var filteredResult = result & WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramRunning;

                Debug.WriteLine($"Device state is: {filteredResult.OutputDeviceExecutionState()}.");

                return (filteredResult == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramRunning);
            }
            else
            {
                return false;
            }
        }

        /// <summary>
        /// Check if device execution state is: stopped on type resolution failed.
        /// </summary>
        /// <param name="debugEngine"></param>
        /// <returns></returns>
        public static bool IsDeviceStoppedOnTypeResolutionFailed(this Engine debugEngine)
        {
            var result = debugEngine.GetExecutionMode();

            if (result != WireProtocol.Commands.DebuggingExecutionChangeConditions.State.Unknown)
            {
                var filteredResult = result & WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ResolutionFailed;

                Debug.WriteLine($"Device state is: {filteredResult.OutputDeviceExecutionState()}.");

                return (filteredResult == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ResolutionFailed);
            }
            else
            {
                return false;
            }
        }

        internal static string OutputDeviceExecutionState(this WireProtocol.Commands.DebuggingExecutionChangeConditions.State state)
        {
            if (state == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.Unknown)
            {
               return "unknown";
            }
            else if (state == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.Initialize)
            {
               return "initialized";
            }
            else if ((state & WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramRunning) == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramRunning)
            {
                if ((state & WireProtocol.Commands.DebuggingExecutionChangeConditions.State.Stopped) == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.Stopped)
                {
                   return "running a program **BUT** execution is stopped";
                }
                else
                {
                   return "running a program ";
                }
            }
            else if ((state & WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramExited) == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramExited)
            {
                if ((state & WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ResolutionFailed) == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ResolutionFailed)
                {
                    return "couldn't start execution because type resolution has failed";
                }
                else
                {
                    return "idle after exiting a program execution or start-up failure";
                }
            }

            // should NEVER get here
            return "";
        }
    }
}
