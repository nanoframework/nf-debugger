//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.Extensions
{
    public static class TaskExtensions
    {
        /// <summary>
        /// Extension to tell the compiler that we actually use the instance for something. This is that the "fire-and-forget" mechanism is being used intentionally.
        /// </summary>
        /// <param name="task"></param>
        public static void FireAndForget(this Task task)
        {
        }

        /// <summary>
        /// Extension to tell the compiler that we actually use the instance for something. This is that the "fire-and-forget" mechanism is being used intentionally.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="task"></param>
        public static void FireAndForget<T>(this Task<T> task)
        {
        }

        public static async Task<T> CancelAfterAsync<T>(this Task<T> task, int timeoutMilliseconds, CancellationTokenSource taskCts)
        {
            // sanity check for reasonable timeout values
            if (timeoutMilliseconds < 0 || (timeoutMilliseconds > 0 && timeoutMilliseconds < 100))
            {
                throw new ArgumentOutOfRangeException();
            }

            var timerCts = new CancellationTokenSource();
            if (await Task.WhenAny(task, Task.Delay(timeoutMilliseconds, timerCts.Token)) == task)
            {
                // task completed, get rid of timer
                timerCts.Cancel();
            }
            else
            {
                // timer completed, cancel task
                taskCts.Cancel();
            }

            // caller should test for exceptions or task cancellation
            return await task;
        }
    }
}
