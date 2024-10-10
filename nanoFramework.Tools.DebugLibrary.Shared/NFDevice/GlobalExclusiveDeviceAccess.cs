// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Threading;
using nanoFramework.Tools.Debugger.PortTcpIp;

namespace nanoFramework.Tools.Debugger.NFDevice
{
    /// <summary>
    /// Code that wants to access a device should use this system-wide exclusive access while
    /// communicating to a device to prevent that another nanoFramework tool also wants to
    /// communicate with the device. The public methods return an instance of <see cref="GlobalExclusiveDeviceAccess"/>
    /// if the access has been granted; that instance has to be disposed of when exclusive
    /// access is no longer needed.
    /// </summary>
    public sealed class GlobalExclusiveDeviceAccess : IDisposable
    {
        #region Fields

        /// <summary>
        ///  Base name for the system-wide mutex that controls access to a device connected to a COM port.
        /// </summary>
        private const string MutexBaseName = @"276545121198496AADD346A60F14EF8D_";
        private static readonly Dictionary<string, (AsyncLocal<GlobalExclusiveDeviceAccess> instance, Semaphore mutex)> s_locks = [];
        private readonly Semaphore _mutex;
        private int _lockCount;
        private readonly string _portInstanceId;

        #endregion

        #region Methods

        /// <summary>
        /// Get exclusive access to a connected device to communicate with that device.
        /// </summary>
        /// <param name="device">The connected device.</param>
        /// <param name="millisecondsTimeout">Maximum time in milliseconds to wait for exclusive access</param>
        /// <param name="cancellationToken">Cancellation token that can be cancelled to stop/abort waiting for the exclusive access.</param>
        /// <returns>Returns an instance of <see cref="GlobalExclusiveDeviceAccess"/> if exclusive access has been granted.
        /// Returns <c>null</c> if exclusive access cannot be obtained within <paramref name="millisecondsTimeout"/>,
        /// or if <paramref name="cancellationToken"/> was cancelled.</returns>
        public static GlobalExclusiveDeviceAccess TryGet(
            NanoDeviceBase device,
            int millisecondsTimeout = Timeout.Infinite,
            CancellationToken? cancellationToken = null)
        {
            return GetOrCreate(
                device.ConnectionPort.InstanceId,
                millisecondsTimeout,
                cancellationToken);
        }

        /// <summary>
        /// Get exclusive access to a device connected to the specified port, to communicate with that device.
        /// </summary>
        /// <param name="port">The port the device is connected to.</param>
        /// <param name="millisecondsTimeout">Maximum time in milliseconds to wait for exclusive access</param>
        /// <param name="cancellationToken">Cancellation token that can be cancelled to stop/abort waiting for the exclusive access.</param>
        /// <returns>Returns an instance of <see cref="GlobalExclusiveDeviceAccess"/> if exclusive access has been granted.
        /// Returns <c>null</c> if exclusive access cannot be obtained within <paramref name="millisecondsTimeout"/>,
        /// or if <paramref name="cancellationToken"/> was cancelled.</returns>
        public static GlobalExclusiveDeviceAccess TryGet(
            IPort port,
            int millisecondsTimeout = Timeout.Infinite,
            CancellationToken? cancellationToken = null)
        {
            return GetOrCreate(
                port.InstanceId,
                millisecondsTimeout,
                cancellationToken);
        }

        /// <summary>
        /// Get exclusive access to a device connected to the specified serial port, to communicate with that device.
        /// </summary>
        /// <param name="serialPort">The serial port the device is connected to.</param>
        /// <param name="millisecondsTimeout">Maximum time in milliseconds to wait for exclusive access</param>
        /// <param name="cancellationToken">Cancellation token that can be cancelled to stop/abort waiting for the exclusive access.</param>
        /// <returns>Returns an instance of <see cref="GlobalExclusiveDeviceAccess"/> if exclusive access has been granted.
        /// Returns <c>null</c> if exclusive access cannot be obtained within <paramref name="millisecondsTimeout"/>,
        /// or if <paramref name="cancellationToken"/> was cancelled.</returns>
        public static GlobalExclusiveDeviceAccess TryGet(
            string serialPort,
            int millisecondsTimeout = Timeout.Infinite,
            CancellationToken? cancellationToken = null)
        {
            return GetOrCreate(
                serialPort,
                millisecondsTimeout,
                cancellationToken);
        }

        /// <summary>
        /// Get exclusive access to a device at the specified network address, to communicate with that device.
        /// </summary>
        /// <param name="address">The network address the device is connected to.</param>
        /// <param name="millisecondsTimeout">Maximum time in milliseconds to wait for exclusive access</param>
        /// <param name="cancellationToken">Cancellation token that can be cancelled to stop/abort waiting for the exclusive access.</param>
        /// <returns>Returns an instance of <see cref="GlobalExclusiveDeviceAccess"/> if exclusive access has been granted.
        /// Returns <c>null</c> if exclusive access cannot be obtained within <paramref name="millisecondsTimeout"/>,
        /// or if <paramref name="cancellationToken"/> was cancelled.</returns>
        public static GlobalExclusiveDeviceAccess TryGet(
            NetworkDeviceInformation address,
            int millisecondsTimeout = Timeout.Infinite,
            CancellationToken? cancellationToken = null)
        {
            return GetOrCreate(
                $"{address.Host}:{address.Port}",
                millisecondsTimeout,
                cancellationToken);
        }

        #endregion

        #region Implementation

        private static GlobalExclusiveDeviceAccess GetOrCreate(
            string portInstanceId,
            int millisecondsTimeout,
            CancellationToken? cancellationToken)
        {
            if (cancellationToken?.IsCancellationRequested == true)
            {
                return null;
            }

            // If the access lock has been created earlier (in previous statements) and has not yet been disposed,
            // use that lock.
            GlobalExclusiveDeviceAccess result = null;

            lock (s_locks)
            {
                if (s_locks.TryGetValue(
                    portInstanceId,
                    out (AsyncLocal<GlobalExclusiveDeviceAccess>, Semaphore) instance))
                {
                    // Note that the result can still be null, in case the exclusive access was obtained by
                    // code that is not a statement previously executed in the context of the current async-thread.
                    result = instance.Item1?.Value;
                }
            }

            if (result is not null)
            {
                // Use the same lock as in Dispose
                lock (result._mutex)
                {
                    if (result._lockCount == 0)
                    {
                        // It should already have been disposed of. Fix that for good measure.
                        lock (s_locks)
                        {
                            s_locks.Remove(portInstanceId);
                        }

                        result = null;
                    }
                    else
                    {
                        result._lockCount++;
                        return result;
                    }
                }
            }

            CancellationTokenSource timeOutToken = null;

            if (millisecondsTimeout != Timeout.Infinite)
            {
                timeOutToken = new CancellationTokenSource(millisecondsTimeout);
            }

            try
            {
                while (result is null)
                {
                    // Cannot use Mutex as the Mutex must be released on the same thread as it is created.
                    // That may not be the case if this is used in async code.
                    var mutex = new Semaphore(
                        0,
                        1,
                        Environment.OSVersion.Platform == PlatformID.Win32NT
                            // A named Semaphore is only supported on Windows OS, and is global - for all processes.
                            ? $"{MutexBaseName}{portInstanceId}"
                            // On other platforms the access is not inter-process but restricted to the current process.
                            : null,
                        out bool createdNew);

                    if (createdNew)
                    {
                        // This code has created the semaphore, so it has exclusive access
                        result = new GlobalExclusiveDeviceAccess(portInstanceId, mutex);
                    }
                    else
                    {
                        // Wait for the semaphore created elsewhere
                        var waitHandles = new List<WaitHandle>()
                        {
                            // Mutex must be added first
                            mutex
                        };

                        // The problem with a semaphore it that, while waiting, it does not detect if the application
                        // with exclusive access is aborted without releasing the semaphore. The semaphore
                        // has to be re-created for that. If the other application just releases the semaphore,
                        // the wait is ended.
                        var iterationToken = new CancellationTokenSource(1000);
                        waitHandles.Add(iterationToken.Token.WaitHandle);

                        // Add the other tokens as well
                        if (timeOutToken is not null)
                        {
                            waitHandles.Add(timeOutToken.Token.WaitHandle);
                        }

                        if (cancellationToken.HasValue)
                        {
                            waitHandles.Add(cancellationToken.Value.WaitHandle);
                        }

                        try
                        {
                            // Try to get exclusive access to the device.
                            int handleIndex = WaitHandle.WaitAny([.. waitHandles]);

                            if (handleIndex == 0)
                            {
                                // Exclusive access granted as the wait ended because of the mutex
                                result = new GlobalExclusiveDeviceAccess(portInstanceId, mutex);
                            }
                            else if (handleIndex != 1)
                            {
                                // timeOutToken or cancellationToken are cancelled
                                break;
                            }
                        }
                        finally
                        {
                            iterationToken.Dispose();

                            if (result is null)
                            {
                                mutex.Dispose();
                            }
                        }
                    }
                }
            }
            finally
            {
                timeOutToken?.Dispose();
            }

            return result;
        }

        private GlobalExclusiveDeviceAccess(
            string portInstanceId,
            Semaphore mutex)
        {
            _mutex = mutex;
            _lockCount = 1;
            _portInstanceId = portInstanceId;

            var instance = new AsyncLocal<GlobalExclusiveDeviceAccess>
            {
                Value = this
            };

            lock (s_locks)
            {
                s_locks[portInstanceId] = (instance, mutex);
            }
        }

        /// <summary>
        /// Dispose of the exclusive access.
        /// </summary>
        public void Dispose()
        {
            bool removeFromLocks = false;

            lock (_mutex)
            {
                _lockCount--;

                if (_lockCount == 0)
                {
                    _mutex.Release();
                    _mutex.Dispose();
                    removeFromLocks = true;
                }
            }

            if (removeFromLocks)
            {
                lock (s_locks)
                {
                    s_locks.Remove(_portInstanceId);
                }
            }
        }

        #endregion
    }
}
