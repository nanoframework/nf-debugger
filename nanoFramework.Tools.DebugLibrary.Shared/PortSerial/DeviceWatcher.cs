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
        private bool _started = false;
        private List<string> _ports;
        private Thread _threadWatch = null;
        private readonly PortSerialManager _ownerManager;

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
        /// <param name="numDevicesAdded">Number of devices added</param>
        public delegate void EventAllNewDevicesAdded(object sender, int numDevicesAdded);

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
        public void Start(ICollection<string> portExclusionList = null)
        {
            if (!_started)
            {
                var exclusionList = portExclusionList?.ToList();
                _threadWatch = new Thread(() =>
                {
                    StartWatcher(exclusionList);
                })
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Lowest
                };

                _threadWatch.Start();
            }
        }

        private void StartWatcher(ICollection<string> portExclusionList)
        {
            _ownerManager.OnLogMessageAvailable($"PortSerial device watcher started @ Thread {_threadWatch.ManagedThreadId} [ProcessID: {Process.GetCurrentProcess().Id}]");

            _ports = new List<string>();

            _started = true;

            Status = DeviceWatcherStatus.Started;

            while (_started)
            {
                try
                {
                    var ports = GetPortNames(portExclusionList);

                    // check for ports that departed 
                    List<string> portsToRemove = new();

                    foreach (var port in _ports)
                    {
                        if (!ports.Contains(port))
                        {
                            portsToRemove.Add(port);
                        }
                    }

                    // process ports that have departed 
                    foreach (var port in portsToRemove)
                    {
                        if (_ports.Contains(port))
                        {
                            _ports.Remove(port);
                            Removed?.Invoke(this, port);
                        }
                    }

                    // process ports that have arrived
                    var tasks = new List<Task>();
                    foreach (var port in ports)
                    {
                        if (!_ports.Contains(port))
                        {
                            _ports.Add(port);
                            if (Added is not null)
                            {
                                if (PortSerialManager.GetRegisteredDevice(port) is null)
                                {
                                    tasks.Add(Task.Run(async () =>
                                    {
                                        await Task.Yield(); // Force true async running
                                        GlobalExclusiveDeviceAccess.CommunicateWithDevice(
                                            port,
                                            () => Added.Invoke(this, port)
                                        );
                                    }));
                                }
                            }
                        }
                    }
                    if (tasks.Count > 0)
                    {
                        Task.WaitAll(tasks.ToArray());
                    }
                    AllNewDevicesAdded?.Invoke(this, tasks.Count);

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

            _ownerManager.OnLogMessageAvailable($"PortSerial device watcher stopped @ Thread {_threadWatch.ManagedThreadId}");

            Status = DeviceWatcherStatus.Stopped;
        }

        /// <summary>
        /// Gets the list of serial ports.
        /// </summary>
        /// <returns>The list of serial ports.</returns>
        public static List<string> GetPortNames(ICollection<string> exclusionList = null)
        {

            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? GetPortNames_Linux(exclusionList)
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? GetPortNames_OSX(exclusionList)
                : RuntimeInformation.IsOSPlatform(OSPlatform.Create("FREEBSD")) ? GetPortNames_FreeBSD(exclusionList)
                : GetPortNames_Windows(exclusionList);
        }

        private static List<string> GetPortNames_Linux(ICollection<string> exclusionList)
        {
            List<string> ports = new List<string>();

            string[] ttys = System.IO.Directory.GetFiles("/dev/", "tty*");
            foreach (string dev in ttys)
            {
                if (exclusionList?.Contains(dev) ?? false)
                {
                    continue;
                }
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

        private static List<string> GetPortNames_OSX(ICollection<string> exclusionList)
        {
            List<string> ports = new List<string>();

            foreach (string name in Directory.GetFiles("/dev", "tty.usbserial*"))
            {
                if (exclusionList?.Contains(name) ?? false)
                {
                    continue;
                }

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
                if (exclusionList?.Contains(name) ?? false)
                {
                    continue;
                }

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

        private static List<string> GetPortNames_FreeBSD(ICollection<string> exclusionList)
        {
            List<string> ports = new List<string>();

            foreach (string name in Directory.GetFiles("/dev", "ttyd*"))
            {
                if (exclusionList?.Contains(name) ?? false)
                {
                    continue;
                }
                if (!name.EndsWith(".init", StringComparison.Ordinal) && !name.EndsWith(".lock", StringComparison.Ordinal))
                {
                    ports.Add(name);
                }
            }

            foreach (string name in Directory.GetFiles("/dev", "cuau*"))
            {
                if (exclusionList?.Contains(name) ?? false)
                {
                    continue;
                }
                if (!name.EndsWith(".init", StringComparison.Ordinal) && !name.EndsWith(".lock", StringComparison.Ordinal))
                {
                    ports.Add(name);
                }
            }

            return ports;
        }

        private static List<string> GetPortNames_Windows(ICollection<string> exclusionList)
        {
            const string FindFullPathPattern = @"\\\\\?\\([\w]*)#([\w&]*)#([\w&]*)";
            const string RegExPattern = @"\\Device\\([a-zA-Z]*)(\d)";
            List<string> portNames = new List<string>();
            try
            {
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
                                        if (portName != null && !(exclusionList?.Contains(portName) ?? false))
                                        {
                                            portNames.Add(portName);
                                        }
                                    }
                                }
                            }
                            else
                            {
                                string portName = (string)allPorts.GetValue(port);
                                if (portName != null)
                                {
                                    if (exclusionList?.Contains(portName) ?? false)
                                    {
                                        continue;
                                    }

                                    // discard known system and other rogue devices
                                    // Get the full qualified name of the device
                                    string deviceFullPath = (string)deviceFullPaths.GetValue(portName);
                                    if (deviceFullPath != null)
                                    {
                                        // make  it upper case for comparison
                                        var deviceFULLPATH = deviceFullPath.ToUpperInvariant();

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
                                            continue;
                                        }

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
        public void Stop()
        {
            _started = false;
            Status = DeviceWatcherStatus.Stopping;
        }

        /// <summary>
        /// Disposes the watcher.
        /// </summary>
        public void Dispose()
        {
            Stop();

            while (Status != DeviceWatcherStatus.Started)
            {
                Thread.Sleep(50);
            }

            _threadWatch = null;
        }
    }
}
