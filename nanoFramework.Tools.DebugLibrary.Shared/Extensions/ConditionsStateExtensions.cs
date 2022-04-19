//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using Polly;
using System;
using System.Diagnostics;

namespace nanoFramework.Tools.Debugger.Extensions
{
    public static class ConditionsStateExtensions
    {
        /// <summary>
        /// Check if device state is Initialized state (see remarks).
        /// </summary>
        /// <param name="state"></param>
        /// <returns><see langword="true"/> if the device is in initialized state. <see langword="false"/> otherwise.</returns>
        /// <remarks>
        /// A device is in initialized state if it's not running a program or if the program execution is stopped (after having running one).
        /// </remarks>
        public static bool IsDeviceInInitializeState(this Commands.DebuggingExecutionChangeConditions.State state)
        {
            // check for unknown state
            if(state == Commands.DebuggingExecutionChangeConditions.State.Unknown)
            {
                return false;
            }

            // engine is in initialized state if it's not running a program or if the program execution is stopped (after having running one)
            var filteredResult = state & (Commands.DebuggingExecutionChangeConditions.State.ProgramExited | Commands.DebuggingExecutionChangeConditions.State.ProgramRunning);

            Debug.WriteLine($"Device state is: {filteredResult.OutputDeviceExecutionState()}.");

            return (filteredResult == Commands.DebuggingExecutionChangeConditions.State.Initialize);
        }

        /// <summary>
        /// Check if device execution state is: program exited.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public static bool IsDeviceInExitedState(this Commands.DebuggingExecutionChangeConditions.State state)
        {
            // check for unknown state
            if (state == Commands.DebuggingExecutionChangeConditions.State.Unknown)
            {
                return false;
            }

            var filteredResult = state & WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramExited;

            Debug.WriteLine($"Device state is: {filteredResult.OutputDeviceExecutionState()}.");

            return (filteredResult == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramExited);
        }

        /// <summary>
        /// Check if device execution state is: program running.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public static bool IsDeviceInProgramRunningState(this Commands.DebuggingExecutionChangeConditions.State state)
        {
            // check for unknown state
            if (state == Commands.DebuggingExecutionChangeConditions.State.Unknown)
            {
                return false;
            }
            
            var filteredResult = state & WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramRunning;

            Debug.WriteLine($"Device state is: {filteredResult.OutputDeviceExecutionState()}.");

            return (filteredResult == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ProgramRunning);
        }

        /// <summary>
        /// Check if device execution state is: stopped on type resolution failed.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public static bool IsDeviceStoppedOnTypeResolutionFailed(this Commands.DebuggingExecutionChangeConditions.State state)
        {
            // check for unknown state
            if (state == Commands.DebuggingExecutionChangeConditions.State.Unknown)
            {
                return false;
            }
            
            var filteredResult = state & WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ResolutionFailed;

            Debug.WriteLine($"Device state is: {filteredResult.OutputDeviceExecutionState()}.");

            return (filteredResult == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.ResolutionFailed);
        }

        /// <summary>
        /// Check if device execution state is: stopped.
        /// </summary>
        /// <param name="state"></param>
        /// <returns></returns>
        public static bool IsDeviceInStoppedState(this Commands.DebuggingExecutionChangeConditions.State state)
        {
            // check for unknown state
            if (state == Commands.DebuggingExecutionChangeConditions.State.Unknown)
            {
                return false;
            }

            var filteredResult = state & WireProtocol.Commands.DebuggingExecutionChangeConditions.State.Stopped;

            Debug.WriteLine($"Device state is: {filteredResult.OutputDeviceExecutionState()}.");

            return (filteredResult == WireProtocol.Commands.DebuggingExecutionChangeConditions.State.Stopped);
        }
    }
}
