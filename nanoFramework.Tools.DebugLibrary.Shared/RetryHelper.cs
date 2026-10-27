// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Threading;

namespace nanoFramework.Tools.Debugger
{
    /// <summary>
    /// Runs an operation again, after a delay, when it fails.
    /// </summary>
    internal static class RetryHelper
    {
        /// <summary>
        /// Runs <paramref name="action"/>, retrying it when it fails.
        /// </summary>
        /// <typeparam name="T">Type of the result of <paramref name="action"/>.</typeparam>
        /// <param name="action">The operation to run.</param>
        /// <param name="retryCount">Maximum number of retries, after the first attempt.</param>
        /// <param name="delayProvider">Gives the delay before a retry, from the retry number (1 for the first retry).</param>
        /// <param name="shouldRetryOnException">Tells if an exception thrown by <paramref name="action"/> is worth a retry.
        /// <see langword="null"/> to not retry on exceptions.</param>
        /// <param name="shouldRetryOnResult">Tells if a result of <paramref name="action"/> is a failure worth a retry.
        /// <see langword="null"/> to accept any result. An exception thrown by it is handled as if thrown by <paramref name="action"/>.</param>
        /// <param name="onRetry">Called before the delay of each retry, with the exception that caused it
        /// (<see langword="null"/> if it's a result failure), the delay and the retry number.</param>
        /// <returns>The result of the last attempt (which is a result failure if no retries are left).</returns>
        /// <remarks>
        /// When no retries are left, the exception of the last attempt is thrown.
        /// An exception that isn't worth a retry is thrown immediately.
        /// </remarks>
        internal static T Execute<T>(
            Func<T> action,
            int retryCount,
            Func<int, TimeSpan> delayProvider,
            Func<Exception, bool> shouldRetryOnException = null,
            Func<T, bool> shouldRetryOnResult = null,
            Action<Exception, TimeSpan, int> onRetry = null)
        {
            return Execute(
                _ => action(),
                retryCount,
                delayProvider,
                shouldRetryOnException,
                shouldRetryOnResult,
                onRetry,
                CancellationToken.None);
        }

        /// <summary>
        /// Runs <paramref name="action"/>, retrying it when it fails, until <paramref name="cancellationToken"/> is cancelled.
        /// </summary>
        /// <typeparam name="T">Type of the result of <paramref name="action"/>.</typeparam>
        /// <param name="action">The operation to run. It gets <paramref name="cancellationToken"/>.</param>
        /// <param name="retryCount">Maximum number of retries, after the first attempt.</param>
        /// <param name="delayProvider">Gives the delay before a retry, from the retry number (1 for the first retry).</param>
        /// <param name="shouldRetryOnException">Tells if an exception thrown by <paramref name="action"/> is worth a retry.
        /// <see langword="null"/> to not retry on exceptions.</param>
        /// <param name="shouldRetryOnResult">Tells if a result of <paramref name="action"/> is a failure worth a retry.
        /// <see langword="null"/> to accept any result. An exception thrown by it is handled as if thrown by <paramref name="action"/>.</param>
        /// <param name="onRetry">Called before the delay of each retry, with the exception that caused it
        /// (<see langword="null"/> if it's a result failure), the delay and the retry number.</param>
        /// <param name="cancellationToken">Aborts the retries (and the delays between them): an <see cref="OperationCanceledException"/> is thrown.</param>
        /// <returns>The result of the last attempt (which is a result failure if no retries are left).</returns>
        /// <remarks>
        /// When no retries are left, the exception of the last attempt is thrown.
        /// An exception that isn't worth a retry is thrown immediately.
        /// </remarks>
        internal static T Execute<T>(
            Func<CancellationToken, T> action,
            int retryCount,
            Func<int, TimeSpan> delayProvider,
            Func<Exception, bool> shouldRetryOnException,
            Func<T, bool> shouldRetryOnResult,
            Action<Exception, TimeSpan, int> onRetry,
            CancellationToken cancellationToken)
        {
            for (int retry = 1; ; retry++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                Exception failure;

                try
                {
                    T result = action(cancellationToken);

                    // the condition first: it may have to run even if no retries are left (e.g. it records why the action failed)
                    if (shouldRetryOnResult is null
                        || !shouldRetryOnResult(result)
                        || retry > retryCount)
                    {
                        return result;
                    }

                    failure = null;
                }
                catch (Exception ex)
                {
                    if (shouldRetryOnException is null
                        || !shouldRetryOnException(ex)
                        || retry > retryCount)
                    {
                        throw;
                    }

                    failure = ex;
                }

                TimeSpan delay = delayProvider(retry);

                onRetry?.Invoke(failure, delay, retry);

                if (cancellationToken.CanBeCanceled)
                {
                    if (cancellationToken.WaitHandle.WaitOne(delay))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }
                }
                else
                {
                    Thread.Sleep(delay);
                }
            }
        }
    }
}
