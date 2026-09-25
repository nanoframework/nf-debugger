// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
    public class DeviceWatcher : IDisposable
    {
        private volatile bool _started = false;
        private volatile Thread _threadWatch = null;
        private readonly PortSerialManager _ownerManager;
        private readonly object _lifecycleLock = new object();

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
                    if (_started)
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
            LogMessage($"PortSerial device watcher started @ Thread {Thread.CurrentThread.ManagedThreadId} [ProcessID: {Process.GetCurrentProcess().Id}]");

            var watchedPorts = new Dictionary<string, CancellationTokenSource>();

            #region Support for the AllNewDevicesAdded event
            object allNewDevicesLock = new object();
            int allNewDevicesCandidateCount = 0;
            bool anyOfAllNewDevicesDetected = false;
            bool raiseAllNewDevicesAdded = true;

            void UpdateAllNewDevices(bool isOneOfAllNewDevices)
            {
                lock (allNewDevicesLock)
                {
                    if (isOneOfAllNewDevices)
                    {
                        anyOfAllNewDevicesDetected = true;
                    }
                    if (--allNewDevicesCandidateCount == 0)
                    {
                        if (anyOfAllNewDevicesDetected && raiseAllNewDevicesAdded)
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
                        anyOfAllNewDevicesDetected = false;
                        raiseAllNewDevicesAdded = false;
                    }
                }
            }
            #endregion

            // status is set to Started by Start(), before this thread runs
            while (_started && IsCurrentWatcherThread)
            {
                try
                {
                    var ports = new List<string>();
                    lock (portsToExclude)
                    {
                        ports.AddRange(from p in GetPortNames()
                                       where !portsToExclude.Contains(p)
                                       select p);
                    }

                    // check for ports that departed 
                    List<string> portsToRemove = new();

                    foreach (var port in watchedPorts)
                    {
                        if (!ports.Contains(port.Key))
                        {
                            port.Value.Cancel();
                            portsToRemove.Add(port.Key);
                        }
                    }

                    // process ports that have departed 
                    foreach (var port in portsToRemove)
                    {
                        if (watchedPorts.ContainsKey(port))
                        {
                            watchedPorts.Remove(port);
                            Removed?.Invoke(this, port);
                        }
                    }

                    // process ports that have arrived
                    foreach (var port in ports)
                    {
                        if (!watchedPorts.ContainsKey(port))
                        {
                            var cancelWaitForAccess = new CancellationTokenSource();
                            watchedPorts[port] = cancelWaitForAccess;
                            if (Added is not null)
                            {
                                if (PortSerialManager.GetRegisteredDevice(port) is null)
                                {
                                    bool isOneOfAllNewDevices = true;
                                    bool shouldRaiseAllNewDevicesAdded = false;
                                    lock (allNewDevicesLock)
                                    {
                                        if (raiseAllNewDevicesAdded && allNewDevicesCandidateCount == 0)
                                        {
                                            raiseAllNewDevicesAdded = false;
                                            shouldRaiseAllNewDevicesAdded = true;
                                        }
                                    }
                                    if (shouldRaiseAllNewDevicesAdded)
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

                                    Task.Run(async () =>
                                    {
                                        // Force true async running
                                        await Task.Yield();

                                        // Wait a short time, so that the AllNewDevices event does not have to
                                        // be delayed for ports that are inaccessible.
                                        var exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(port, 1000, cancelWaitForAccess.Token);
                                        if (exclusiveAccess is null)
                                        {
                                            // It took too long to get access
                                            if (isOneOfAllNewDevices)
                                            {
                                                // Do not wait for the port to send the AllNewDevicesAdded
                                                isOneOfAllNewDevices = false;
                                                UpdateAllNewDevices(isOneOfAllNewDevices);
                                            }

                                            if (cancelWaitForAccess.IsCancellationRequested)
                                            {
                                                // The port disappeared
                                                return;
                                            }

                                            // Now wait forever for the port to become available
                                            exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(port, cancellationToken: cancelWaitForAccess.Token);
                                            if (exclusiveAccess is null)
                                            {
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
                                            if (isOneOfAllNewDevices)
                                            {
                                                UpdateAllNewDevices(isOneOfAllNewDevices);
                                            }
                                        }
                                    });
                                }
                            }
                        }
                    }

                    // If no new device candidates were queued during this first scan pass (either because
                    // there are no ports at all, or all visible ports are already registered), the
                    // UpdateAllNewDevices callback will never be called and AllNewDevicesAdded would
                    // never fire. Fire it explicitly here to unblock enumeration completion.
                    lock (allNewDevicesLock)
                    {
                        if (raiseAllNewDevicesAdded && allNewDevicesCandidateCount == 0)
                        {
                            raiseAllNewDevicesAdded = false;
                            try
                            {
                                AllNewDevicesAdded?.Invoke(this);
                            }
                            catch
                            {
                                // The device watcher must continue
                            }
                        }
                    }

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

            foreach (var source in watchedPorts.Values)
            {
                source.Cancel();
            }

            LogMessage($"PortSerial device watcher stopped @ Thread {Thread.CurrentThread.ManagedThreadId}");
        }

        /// <summary>
        /// Gets the list of serial ports.
        /// </summary>
        /// <returns>The list of serial ports that may be connected to a nanoDevice.</returns>
        public static List<string> GetPortNames()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? GetPortNames_Linux()
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? GetPortNames_OSX()
                : RuntimeInformation.IsOSPlatform(OSPlatform.Create("FREEBSD")) ? GetPortNames_FreeBSD()
                : GetPortNames_Windows();
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

        private static List<string> GetPortNames_Windows()
        {
            const string FindFullPathPattern = @"\\\\\?\\([\w]*)#([\w&]*)#([\w&]*)";
            const string RegExPattern = @"\\Device\\([a-zA-Z]*)(\d)";
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
                RegistryKey allPorts = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
                RegistryKey deviceFullPaths = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\COM Name Arbiter\Devices");
                if (allPorts != null)
                {
                    // Then gets all the names, they are like \Device\BthModem0 \Device\Silabser0 etc,
                    foreach (var port in allPorts.GetValueNames())
                    {
                        var portNameDetails = Regex.Match(port, RegExPattern);
                        if (portNameDetails.Success)
                        {
                            RegistryKey activePorts = Registry.LocalMachine.OpenSubKey($"SYSTEM\\CurrentControlSet\\Services\\{portNameDetails.Groups[1]}\\Enum");
                            if (activePorts != null)
                            {
                                // If the device is still plugged, it should appear as valid here, if not present, it means, the device has been disconnected
                                string portDescription = (string)activePorts.GetValue($"{portNameDetails.Groups[2]}");
                                int numPorts = (int)activePorts.GetValue("Count");
                                if ((portDescription == null) && (numPorts > 0))
                                {
                                    portDescription = (string)activePorts.GetValue($"{numPorts - 1}");
                                }

                                if (portDescription != null)
                                {
                                    RegistryKey portKeyInfo = Registry.LocalMachine.OpenSubKey($"SYSTEM\\CurrentControlSet\\Enum\\{portDescription}\\Device Parameters");
                                    if (portKeyInfo != null)
                                    {
                                        string portName = (string)allPorts.GetValue(port);
                                        if (portName != null
                                            && !IsSpecialPort((string)deviceFullPaths.GetValue(portName)))
                                        {
                                            portNames.Add(portName);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                string portName = (string)allPorts.GetValue(port);
                                string deviceFullPath = (string)deviceFullPaths.GetValue(portName);
                                if (deviceFullPath != null)
                                {
                                    if (IsSpecialPort(deviceFullPath))
                                    {
                                        // don't even bother with this one
                                        continue;
                                    }

                                    // Get the full qualified name of the device
                                    var devicePathDetail = Regex.Match(deviceFullPath.Replace("+", "&"), FindFullPathPattern);
                                    if ((devicePathDetail.Success) && (devicePathDetail.Groups.Count == 4))
                                    {
                                        string devicePath = deviceFullPath.Split('#')[1];

                                        RegistryKey device = Registry.LocalMachine.OpenSubKey($"SYSTEM\\CurrentControlSet\\Enum\\{devicePathDetail.Groups[1]}\\{devicePath}\\{devicePathDetail.Groups[3]}");
                                        if (device != null)
                                        {
                                            string service = (string)device.GetValue("Service");
                                            if (service != null)
                                            {
                                                activePorts = Registry.LocalMachine.OpenSubKey($"SYSTEM\\CurrentControlSet\\Services\\{service}\\Enum");
                                                if (activePorts != null)
                                                {
                                                    // If the device is still plugged, it should appear as valid here, if not present, it means, the device has been disconnected                                                        
                                                    portNames.Add(portName);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
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
        /// Stops the watcher and waits for the watcher thread to exit.
        /// </summary>
        /// <param name="millisecondsTimeout">Maximum time to wait, or <see cref="Timeout.Infinite"/>.</param>
        /// <returns><see langword="true"/> if the watcher thread is not running when this call returns.
        /// When called from the watcher thread itself (e.g. from an event handler) this doesn't wait and returns <see langword="false"/>.</returns>
        internal bool StopAndWait(int millisecondsTimeout)
        {
            Stop();

            var thread = _threadWatch;

            if (thread is null)
            {
                return true;
            }

            if (thread == Thread.CurrentThread)
            {
                // don't wait for our thread
                return false;
            }

            return thread.Join(millisecondsTimeout);
        }

        /// <summary>
        /// Disposes the watcher.
        /// </summary>
        public void Dispose()
        {
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
