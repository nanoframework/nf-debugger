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
    /// communicate with the device.
    /// </summary>
    public static class GlobalExclusiveDeviceAccess
    {
        #region Fields
        /// <summary>
        ///  Base name for the system-wide mutex that controls access to a device connected to a COM port.
        /// </summary>
        private const string MutexBaseName = "276545121198496AADD346A60F14EF8D_";
        #endregion

        #region Methods
        /// <summary>
        /// Communicate with a serial device and ensure the code to be executed as exclusive access to the device.
        /// </summary>
        /// <param name="serialPort">The serial port the device is connected to.</param>
        /// <param name="communication">Code to execute while having exclusive access to the device</param>
        /// <param name="millisecondsTimeout">Maximum time in milliseconds to wait for exclusive access</param>
        /// <param name="cancellationToken">Cancellation token that can be cancelled to stop/abort running the <paramref name="communication"/>.
        /// This method does not stop/abort execution of <paramref name="communication"/> after it has been started.</param>
        /// <returns>Indicates whether the <paramref name="communication"/> has been executed. Returns <c>false</c> if exclusive access
        /// cannot be obtained within <paramref name="millisecondsTimeout"/>, or if <paramref name="cancellationToken"/> was cancelled
        /// before the <paramref name="communication"/> has been started.</returns>
        public static bool CommunicateWithDevice(string serialPort, Action communication, int millisecondsTimeout = Timeout.Infinite, CancellationToken? cancellationToken = null)
        {
            return DoCommunicateWithDevice(serialPort, communication, millisecondsTimeout, cancellationToken);
        }

        /// <summary>
        /// Communicate with a device accessible via the network and ensure the code to be executed as exclusive access to the device.
        /// </summary>
        /// <param name="address">The network address the device is connected to.</param>
        /// <param name="communication">Code to execute while having exclusive access to the device</param>
        /// <param name="millisecondsTimeout">Maximum time in milliseconds to wait for exclusive access</param>
        /// <param name="cancellationToken">Cancellation token that can be cancelled to stop/abort running the <paramref name="communication"/>.
        /// This method does not stop/abort execution of <paramref name="communication"/> after it has been started.</param>
        /// <returns>Indicates whether the <paramref name="communication"/> has been executed. Returns <c>false</c> if exclusive access
        /// cannot be obtained within <paramref name="millisecondsTimeout"/>, or if <paramref name="cancellationToken"/> was cancelled
        /// before the <paramref name="communication"/> has been started.</returns>
        public static bool CommunicateWithDevice(NetworkDeviceInformation address, Action communication, int millisecondsTimeout = Timeout.Infinite, CancellationToken? cancellationToken = null)
        {
            return DoCommunicateWithDevice($"{address.Host}:{address.Port}", communication, millisecondsTimeout, cancellationToken);
        }
        #endregion

        #region Implementation
        private static bool DoCommunicateWithDevice(string connectionKey, Action communication, int millisecondsTimeout, CancellationToken? cancellationToken)
        {
            for (var retry = true; retry;)
            {
                retry = false;

                var waitHandles = new List<WaitHandle>();
                var mutex = new Mutex(false, $"{MutexBaseName}_{connectionKey}");
                waitHandles.Add(mutex);

                CancellationTokenSource timeOutToken = null;
                if (millisecondsTimeout > 0 && millisecondsTimeout != Timeout.Infinite)
                {
                    timeOutToken = new CancellationTokenSource(millisecondsTimeout);
                    waitHandles.Add(timeOutToken.Token.WaitHandle);
                }
                if (cancellationToken.HasValue)
                {
                    waitHandles.Add(cancellationToken.Value.WaitHandle);
                }
                try
                {
                    if (WaitHandle.WaitAny(waitHandles.ToArray()) == 0)
                    {
                        communication();
                        return true;
                    }
                }
                catch (AbandonedMutexException)
                {
                    // While this process is waiting on a mutex, the process that owned the mutex has been terminated
                    // without properly releasing the mutex.
                    // Try again, if this is the only remaining process it will re-create the mutex and get exclusive access.
                    retry = true;
                }
                finally
                {
                    mutex.ReleaseMutex();
                    timeOutToken?.Dispose();
                }
            }
            return false;
        }
        #endregion
    }
}
