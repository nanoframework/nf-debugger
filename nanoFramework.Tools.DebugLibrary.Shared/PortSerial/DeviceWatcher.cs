// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
#if NET5_0_OR_GREATER
using System.Runtime.Versioning;
#endif
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;
using nanoFramework.Tools.Debugger.NFDevice;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    /// <summary>
    /// Device watcher.
    /// </summary>
    public partial class DeviceWatcher : IDisposable
    {
        private volatile bool _started = false;
        private volatile Thread _threadWatch = null;
        private readonly PortSerialManager _ownerManager;
        private readonly object _lifecycleLock = new object();
        private bool _disposed = false;

        // Added notifications queued by the watcher and not finished yet (they outlive the watcher thread)
        private int _pendingNotifications = 0;
        private readonly object _notificationsLock = new object();

        // set while running one of the Added notifications of a watcher, to detect calls from its event handlers
        // (thread static, not async local: it must not flow into threads or tasks started by the event handlers)
        [ThreadStatic]
        private static DeviceWatcher t_notifyingWatcher;

        /// <summary>
        /// Represents a delegate method that is used to handle the DeviceAdded event.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="port">The port of the device that was added.</param>
        public delegate void EventDeviceAdded(object sender, string port);

        /// <summary>
        /// Raised when a device is added to the system.
        /// </summary>
        public event EventDeviceAdded Added;

        /// <summary>
        /// Represents a delegate method that is used to handle the DeviceRemoved event.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        /// <param name="port">The port of the device that was removed.</param>
        public delegate void EventDeviceRemoved(object sender, string port);

        /// <summary>
        /// Raised when a device is removed from the system.
        /// </summary>
        public event EventDeviceRemoved Removed;

        /// <summary>
        /// Represents a delegate method that is used to handle the AllNewDevicesAdded event.
        /// </summary>
        /// <param name="sender">The object that raised the event.</param>
        public delegate void EventAllNewDevicesAdded(object sender);

        /// <summary>
        /// Raised when all newly discovered devices have been added
        /// </summary>
        public event EventAllNewDevicesAdded AllNewDevicesAdded;

        /// <summary>
        /// Gets or sets the status of the device watcher.
        /// </summary>
        public DeviceWatcherStatus Status { get; internal set; }

        /// <summary>
        /// Constructor for a <see cref="PortSerialManager"/> device watcher class.
        /// </summary>
        /// <param name="owner">The <see cref="PortSerialManager"/> that owns this device watcher.</param>
        public DeviceWatcher(PortSerialManager owner)
        {
            _ownerManager = owner;
        }

        /// <summary>
        /// Starts the device watcher.
        /// </summary>
        /// <param name="portsToExclude">The collection of serial ports to ignore when searching for devices.
        /// Changes in the collection after the start of the device watcher are taken into account.</param>
        public void Start(ICollection<string> portsToExclude = null)
        {
            while (true)
            {
                Thread previousThread;

                lock (_lifecycleLock)
                {
                    // once disposed, Start() has no effect
                    if (_started || _disposed)
                    {
                        return;
                    }

                    previousThread = _threadWatch;

                    if (previousThread is null
                        || !previousThread.IsAlive
                        || previousThread == Thread.CurrentThread)
                    {
                        StartWatcherThread(portsToExclude);
                        return;
                    }
                }

                previousThread.Join();
            }
        }

        // must be called while holding _lifecycleLock
        private void StartWatcherThread(ICollection<string> portsToExclude)
        {
            try
            {
                _threadWatch = new Thread(() =>
                {
                    StartWatcher(portsToExclude ?? []);
                })
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Lowest
                };

                Status = DeviceWatcherStatus.Started;
                _started = true;

                _threadWatch.Start();
            }
            catch
            {
                _started = false;
                _threadWatch = null;
                Status = DeviceWatcherStatus.Stopped;

                throw;
            }
        }

        private void StartWatcher(ICollection<string> portsToExclude)
        {
            try
            {
                RunWatcher(portsToExclude);
            }
            finally
            {
                lock (_lifecycleLock)
                {
                    if (IsCurrentWatcherThread)
                    {
                        Status = DeviceWatcherStatus.Stopped;
                    }
                }
            }
        }

        private bool IsCurrentWatcherThread => _threadWatch == Thread.CurrentThread;

#if NET5_0_OR_GREATER
        private static int CurrentProcessId => Environment.ProcessId;
#else
        private static int CurrentProcessId => System.Diagnostics.Process.GetCurrentProcess().Id;
#endif

        private void LogMessage(string message)
        {
            try
            {
                _ownerManager.OnLogMessageAvailable(message);
            }
            catch
            {
                // a faulty log handler must not prevent the watcher from running or stopping
            }
        }

        private void RunWatcher(ICollection<string> portsToExclude)
        {
            LogMessage($"PortSerial device watcher started @ Thread {Environment.CurrentManagedThreadId} [ProcessID: {CurrentProcessId}]");

            // local to this watcher thread, so a restarted watcher never shares it
            var watchedPorts = new Dictionary<string, CancellationTokenSource>();

            // tracks the devices present when this watcher run started, to raise AllNewDevicesAdded once they're all processed
            var initialEnumeration = new InitialEnumeration(RaiseAllNewDevicesAdded);

            try
            {
                // status is set to Started by Start(), before this thread runs
                while (_started && IsCurrentWatcherThread)
                {
                    try
                    {
                        ScanPorts(portsToExclude, watchedPorts, initialEnumeration);

                        Thread.Sleep(200);
                    }
#if DEBUG
                    catch (Exception ex)
#else
                    catch
#endif
                    {
                        // catch all so the watcher can always do it's job
                        // on any exception the thread will get back to the loop or exit on the while loop condition
                    }
                }
            }
            finally
            {
                // candidates still being processed belong to this run: they must not signal a later run
                initialEnumeration.Abandon();

                foreach (var source in watchedPorts.Values)
                {
                    source.Cancel();
                }
            }

            LogMessage($"PortSerial device watcher stopped @ Thread {Environment.CurrentManagedThreadId}");
        }

        private void ScanPorts(
            ICollection<string> portsToExclude,
            Dictionary<string, CancellationTokenSource> watchedPorts,
            InitialEnumeration initialEnumeration)
        {
            List<string> ports;
            lock (portsToExclude)
            {
                ports = GetPortNames().Where(p => !portsToExclude.Contains(p)).ToList();
            }

            ProcessDepartedPorts(ports, watchedPorts);

            ProcessArrivedPorts(ports, watchedPorts, initialEnumeration);

            // first scan pass completed: from now on AllNewDevicesAdded is raised as soon as all
            // the candidates it found are processed (right away if there were none)
            initialEnumeration.CloseFirstPass();
        }

        private void ProcessDepartedPorts(
            List<string> ports,
            Dictionary<string, CancellationTokenSource> watchedPorts)
        {
            // check for ports that departed
            var portsToRemove = watchedPorts.Keys.Where(p => !ports.Contains(p)).ToList();

            foreach (var port in portsToRemove)
            {
                watchedPorts[port].Cancel();
            }

            // process ports that have departed
            foreach (var port in portsToRemove)
            {
                if (watchedPorts.Remove(port))
                {
                    Removed?.Invoke(this, port);
                }
            }
        }

        private void ProcessArrivedPorts(
            List<string> ports,
            Dictionary<string, CancellationTokenSource> watchedPorts,
            InitialEnumeration initialEnumeration)
        {
            foreach (var port in ports.Where(p => !watchedPorts.ContainsKey(p)))
            {
                var cancelWaitForAccess = new CancellationTokenSource();
                watchedPorts[port] = cancelWaitForAccess;

                if (Added is null
                    || PortSerialManager.GetRegisteredDevice(port) is not null)
                {
                    continue;
                }

                // only the devices found by the first scan pass are part of the initial enumeration
                Action releaseCandidate = initialEnumeration.IsFirstPassOpen
                    ? initialEnumeration.AddCandidate()
                    : NoCandidate;

                // counted before queuing, so that StopAndWait() can't miss it
                lock (_notificationsLock)
                {
                    _pendingNotifications++;
                }

                // processing a device blocks for seconds so use a dedicated thread
                Task.Factory.StartNew(
                    () => NotifyDeviceAdded(port, releaseCandidate, cancelWaitForAccess.Token),
                    CancellationToken.None,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default);
            }
        }

        private static void NoCandidate()
        {
            // not part of the initial enumeration, nothing to release
        }

        private void NotifyDeviceAdded(
            string port,
            Action releaseCandidate,
            CancellationToken portDeparted)
        {
            var previousNotifyingWatcher = t_notifyingWatcher;
            t_notifyingWatcher = this;

            try
            {
                // Wait a short time first...
                var exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(port, 1000, portDeparted);

                if (exclusiveAccess is null)
                {
                    // ... a port that is inaccessible (or gone) must not hold up the initial enumeration
                    releaseCandidate();

                    if (portDeparted.IsCancellationRequested)
                    {
                        // the port disappeared
                        return;
                    }

                    // ... then wait forever for the port to become available (or the watcher to stop)
                    exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(port, cancellationToken: portDeparted);

                    if (exclusiveAccess is null)
                    {
                        // the port disappeared
                        return;
                    }
                }

                try
                {
                    Added?.Invoke(this, port);
                }
                finally
                {
                    exclusiveAccess.Dispose();

                    // the device has been processed (no-op if already released)
                    releaseCandidate();
                }
            }
            finally
            {
                t_notifyingWatcher = previousNotifyingWatcher;

                lock (_notificationsLock)
                {
                    if (--_pendingNotifications == 0)
                    {
                        Monitor.PulseAll(_notificationsLock);
                    }
                }
            }
        }

        /// <summary>
        /// Waits for the queued Added notifications to finish.
        /// </summary>
        private bool WaitForNotifications(int millisecondsTimeout)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();

            lock (_notificationsLock)
            {
                while (_pendingNotifications > 0)
                {
                    int remaining = millisecondsTimeout == Timeout.Infinite
                        ? Timeout.Infinite
                        : (int)Math.Max(0, millisecondsTimeout - timer.ElapsedMilliseconds);

                    if (!Monitor.Wait(_notificationsLock, remaining))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Tracks the initial enumeration of a watcher run: the devices found by the first scan pass.
        /// Raises the completion callback once, after the first scan pass and all its candidates have been processed.
        /// </summary>
        private sealed class InitialEnumeration
        {
            private readonly Action _onCompleted;

            // the first scan pass holds one "pending" until it's done, so completion can't be
            // signalled while candidates are still being queued
            private int _pending = 1;
            private int _completed = 0;
            private volatile bool _abandoned = false;

            public InitialEnumeration(Action onCompleted)
            {
                _onCompleted = onCompleted;
            }

            /// <summary>
            /// Gets whether the first scan pass is still in progress. Only accessed from the watcher thread.
            /// </summary>
            public bool IsFirstPassOpen { get; private set; } = true;

            /// <summary>
            /// Adds a candidate to the initial enumeration.
            /// </summary>
            /// <returns>The action to call once the candidate is processed. It can be called more than once, only the first call counts.</returns>
            public Action AddCandidate()
            {
                Interlocked.Increment(ref _pending);

                int released = 0;

                return () =>
                {
                    if (Interlocked.Exchange(ref released, 1) == 0)
                    {
                        Release();
                    }
                };
            }

            /// <summary>
            /// Signals the end of the first scan pass. Only the first call counts.
            /// </summary>
            public void CloseFirstPass()
            {
                if (IsFirstPassOpen)
                {
                    IsFirstPassOpen = false;

                    Release();
                }
            }

            /// <summary>
            /// The watcher run has ended: completion won't be signalled anymore.
            /// </summary>
            public void Abandon() => _abandoned = true;

            private void Release()
            {
                if (Interlocked.Decrement(ref _pending) == 0
                    && Interlocked.Exchange(ref _completed, 1) == 0
                    && !_abandoned)
                {
                    // no locks held here
                    _onCompleted();
                }
            }
        }

        private void RaiseAllNewDevicesAdded()
        {
            try
            {
                AllNewDevicesAdded?.Invoke(this);
            }
            catch
            {
                // The device watcher must continue
            }
        }

        /// <summary>
        /// Gets the list of serial ports.
        /// </summary>
        /// <returns>The list of serial ports that may be connected to a nanoDevice.</returns>
        public static List<string> GetPortNames()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return GetPortNames_Windows();
            }

            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? GetPortNames_Linux()
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? GetPortNames_OSX()
                : RuntimeInformation.IsOSPlatform(OSPlatform.Create("FREEBSD")) ? GetPortNames_FreeBSD()
                : new List<string>();
        }

        private static List<string> GetPortNames_Linux()
        {
            List<string> ports = new List<string>();

            string[] ttys = System.IO.Directory.GetFiles("/dev/", "tty*");
            foreach (string dev in ttys)
            {
                if (dev.StartsWith("/dev/ttyS")
                    || dev.StartsWith("/dev/ttyUSB")
                    || dev.StartsWith("/dev/ttyACM")
                    || dev.StartsWith("/dev/ttyAMA")
                    || dev.StartsWith("/dev/ttyPS")
                    || dev.StartsWith("/dev/serial"))
                {
                    ports.Add(dev);
                }
            }

            return ports;
        }

        private static List<string> GetPortNames_OSX()
        {
            List<string> ports = new List<string>();

            foreach (string name in Directory.GetFiles("/dev", "tty.usbserial*"))
            {
                // We don't want Bluetooth ports
                if (name.ToLower().Contains("bluetooth"))
                {
                    continue;
                }

                // GetFiles can return unexpected results because of 8.3 matching.
                // Like /dev/tty
                if (name.StartsWith("/dev/tty.", StringComparison.Ordinal))
                {
                    ports.Add(name);
                }
            }

            foreach (string name in Directory.GetFiles("/dev", "cu.usbserial*"))
            {
                // We don't want Bluetooth ports
                if (name.ToLower().Contains("bluetooth"))
                {
                    continue;
                }

                if (name.StartsWith("/dev/cu.", StringComparison.Ordinal))
                {
                    ports.Add(name);
                }
            }

            return ports;
        }

        private static List<string> GetPortNames_FreeBSD()
        {
            List<string> ports = new List<string>();

            foreach (string name in Directory.GetFiles("/dev", "ttyd*"))
            {
                if (!name.EndsWith(".init", StringComparison.Ordinal) && !name.EndsWith(".lock", StringComparison.Ordinal))
                {
                    ports.Add(name);
                }
            }

            foreach (string name in Directory.GetFiles("/dev", "cuau*"))
            {
                if (!name.EndsWith(".init", StringComparison.Ordinal) && !name.EndsWith(".lock", StringComparison.Ordinal))
                {
                    ports.Add(name);
                }
            }

            return ports;
        }

#if NET5_0_OR_GREATER
        [SupportedOSPlatform("windows")]
#endif
        private static List<string> GetPortNames_Windows()
        {
            List<string> portNames = new List<string>();
            try
            {
                // discard known system and other rogue devices
                bool IsSpecialPort(string deviceFullPath)
                {
                    if (deviceFullPath is not null)
                    {
                        // make  it upper case for comparison
                        string deviceFULLPATH = deviceFullPath.ToUpperInvariant();

                        if (
                            deviceFULLPATH.StartsWith(@"\\?\ACPI") ||

                            // reported in https://github.com/nanoframework/Home/issues/332
                            // COM ports from Broadcom 20702 Bluetooth adapter
                            deviceFULLPATH.Contains(@"VID_0A5C+PID_21E1") ||

                            // reported in https://nanoframework.slack.com/archives/C4MGGBH1P/p1531660736000055?thread_ts=1531659631.000021&cid=C4MGGBH1P
                            // COM ports from Broadcom 20702 Bluetooth adapter
                            deviceFULLPATH.Contains(@"VID&00010057_PID&0023") ||

                            // reported in Discord channel
                            deviceFULLPATH.Contains(@"VID&0001009E_PID&400A") ||

                            // this seems to cover virtual COM ports from Bluetooth devices
                            deviceFULLPATH.Contains("BTHENUM") ||

                            // this seems to cover virtual COM ports by ELTIMA 
                            deviceFULLPATH.Contains("EVSERIAL")
                            )
                        {
                            // don't even bother with this one
                            return true;
                        }
                    }
                    return false;
                }

                // Gets the list of supposed open ports
                // (Windows can leave stale entries here for devices that are no longer present)
                using RegistryKey allPorts = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
                using RegistryKey deviceFullPaths = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\COM Name Arbiter\Devices");

                if (allPorts != null)
                {
                    // Then gets all the names, they are like \Device\BthModem0 \Device\Silabser0 etc,
                    foreach (var port in allPorts.GetValueNames())
                    {
                        if (allPorts.GetValue(port) is not string portName)
                        {
                            continue;
                        }

                        string deviceFullPath = deviceFullPaths?.GetValue(portName) as string;

                        if (IsSpecialPort(deviceFullPath))
                        {
                            // don't even bother with this one
                            continue;
                        }

                        if (IsPortPresent_Windows(port, portName, deviceFullPath))
                        {
                            portNames.Add(portName);
                        }
                    }
                }
            }
            catch
            {
                // Errors in enumeration can happen
            }

            return portNames;
        }

        /// <summary>
        /// Checks if a serial port listed in SERIALCOMM belongs to a device that is currently present.
        /// A device instance is present when it's listed in the Enum key of its driver service.
        /// </summary>
        /// <param name="deviceName">The device name, like \Device\USBSER000.</param>
        /// <param name="portName">The port name, like COM3.</param>
        /// <param name="deviceFullPath">The device path from the COM Name Arbiter, if available.</param>
#if NET5_0_OR_GREATER
        [SupportedOSPlatform("windows")]
#endif
        private static bool IsPortPresent_Windows(
            string deviceName,
            string portName,
            string deviceFullPath)
        {
            // 1st: the driver service is named after the device (e.g. \Device\USBSER000 -> usbser):
            // look for a present device instance that owns this port name
            var deviceNameDetails = DeviceNameRegex().Match(deviceName);

            if (deviceNameDetails.Success
                && AnyPresentInstance(
                    deviceNameDetails.Groups[1].Value,
                    instanceId => OwnsPort(instanceId, portName)))
            {
                return true;
            }

            // 2nd: get the device instance from its path
            // (e.g. \\?\usb#vid_0483&pid_5740#nano_123#{guid} -> usb\vid_0483&pid_5740\nano_123)
            // and check that it's present for its driver service
            if (deviceFullPath is null
                || !deviceFullPath.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return false;
            }

            var pathParts = deviceFullPath.Substring(4).Split('#');

            if (pathParts.Length < 3)
            {
                return false;
            }

            string deviceInstanceId = $@"{pathParts[0]}\{pathParts[1]}\{pathParts[2]}";

            using RegistryKey device = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{deviceInstanceId}");

            return device?.GetValue("Service") is string service
                   && AnyPresentInstance(
                       service,
                       instanceId => string.Equals(instanceId, deviceInstanceId, StringComparison.OrdinalIgnoreCase));
        }

        // device names in SERIALCOMM, like \Device\USBSER000: the driver service name followed by the device number
#if NET7_0_OR_GREATER
        [GeneratedRegex(@"^\\Device\\([a-zA-Z]+)\d+$")]
        private static partial Regex DeviceNameRegex();
#else
        private static readonly Regex s_deviceNameRegex = new Regex(@"^\\Device\\([a-zA-Z]+)\d+$");

        private static Regex DeviceNameRegex() => s_deviceNameRegex;
#endif

#if NET5_0_OR_GREATER
        [SupportedOSPlatform("windows")]
#endif
        private static bool OwnsPort(
            string instanceId,
            string portName)
        {
            using RegistryKey deviceParameters = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Enum\{instanceId}\Device Parameters");

            return string.Equals(deviceParameters?.GetValue("PortName") as string, portName, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks the device instances that are currently present for a driver service.
        /// </summary>
#if NET5_0_OR_GREATER
        [SupportedOSPlatform("windows")]
#endif
        private static bool AnyPresentInstance(
            string service,
            Func<string, bool> predicate)
        {
            // the Enum key of a service lists the device instances currently present, as values "0" to "Count - 1"
            using RegistryKey presentInstances = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{service}\Enum");

            if (presentInstances?.GetValue("Count") is not int count)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                if (presentInstances.GetValue($"{i}") is string instanceId
                    && predicate(instanceId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Stops the watcher.
        /// </summary>
        /// <remarks>
        /// This call doesn't wait for the watcher to stop. The <see cref="Status"/> changes to
        /// <see cref="DeviceWatcherStatus.Stopped"/> once the watcher has actually stopped.
        /// </remarks>
        public void Stop()
        {
            lock (_lifecycleLock)
            {
                if (!_started)
                {
                    return;
                }

                Status = DeviceWatcherStatus.Stopping;
                _started = false;
            }
        }

        /// <summary>
        /// Stops the watcher and waits for the watcher thread to exit and for the queued Added notifications to finish.
        /// </summary>
        /// <param name="millisecondsTimeout">Maximum time to wait, or <see cref="Timeout.Infinite"/>.</param>
        /// <returns><see langword="true"/> if the watcher thread is not running and no notification is pending when this call returns.
        /// When called from the watcher itself (i.e. from one of its event handlers) this doesn't wait and returns <see langword="false"/>.</returns>
        internal bool StopAndWait(int millisecondsTimeout)
        {
            Stop();

            var thread = _threadWatch;

            if (thread == Thread.CurrentThread
                || t_notifyingWatcher == this)
            {
                // called from an event handler: can't wait for ourselves
                return false;
            }

            var timer = System.Diagnostics.Stopwatch.StartNew();

            if (thread is not null
                && !thread.Join(millisecondsTimeout))
            {
                return false;
            }

            // notifications still running are part of the watcher's work, wait for them too
            return WaitForNotifications(
                millisecondsTimeout == Timeout.Infinite
                    ? Timeout.Infinite
                    : (int)Math.Max(0, millisecondsTimeout - timer.ElapsedMilliseconds));
        }

        /// <summary>
        /// Disposes the watcher.
        /// </summary>
        public void Dispose()
        {
            lock (_lifecycleLock)
            {
                // from now on Start() has no effect
                _disposed = true;
            }

            if (StopAndWait(5000))
            {
                lock (_lifecycleLock)
                {
                    if (_threadWatch?.IsAlive != true)
                    {
                        _threadWatch = null;
                    }
                }
            }
        }
    }
}
