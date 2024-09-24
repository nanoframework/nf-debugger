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
            if (!_started)
            {
                _threadWatch = new Thread(() =>
                {
                    StartWatcher(portsToExclude ?? []);
                })
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Lowest
                };

                _threadWatch.Start();
            }
        }

        private void StartWatcher(ICollection<string> portsToExclude)
        {
            _ownerManager.OnLogMessageAvailable($"PortSerial device watcher started @ Thread {_threadWatch.ManagedThreadId} [ProcessID: {Process.GetCurrentProcess().Id}]");

            _ports = [];

            _started = true;

            int newPortsDetected = 0;
            var newPortsDetectedLock = new object();

            Status = DeviceWatcherStatus.Started;

            while (_started)
            {
                try
                {
                    var ports = new List<string>();
                    lock (portsToExclude)
                    {
                        ports.AddRange(from p in DoGetPortNames()
                                       where !portsToExclude.Contains(p)
                                       select p);
                    }

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
                    foreach (var port in ports)
                    {
                        if (!_ports.Contains(port))
                        {
                            _ports.Add(port);
                            if (Added is not null)
                            {
                                if (PortSerialManager.GetRegisteredDevice(port) is null)
                                {
                                    lock (newPortsDetectedLock)
                                    {
                                        newPortsDetected++;
                                    }

                                    Task.Run(async () =>
                                    {
                                        await Task.Yield(); // Force true async running
                                        GlobalExclusiveDeviceAccess.CommunicateWithDevice(
                                            port,
                                            () => Added.Invoke(this, port)
                                        );
                                        lock (newPortsDetectedLock)
                                        {
                                            if (--newPortsDetected == 0)
                                            {
                                                AllNewDevicesAdded?.Invoke(this);
                                            };
                                        }
                                    });
                                }
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

            _ownerManager.OnLogMessageAvailable($"PortSerial device watcher stopped @ Thread {_threadWatch.ManagedThreadId}");

            Status = DeviceWatcherStatus.Stopped;
        }

        /// <summary>
        /// Gets the list of serial ports.
        /// </summary>
        public List<string> GetPortNames()
        {
            return DoGetPortNames();
        }

        /// <summary>
        /// Gets the list of serial ports.
        /// </summary>
        /// <returns>The list of serial ports that may be connected to a nanoDevice.</returns>
        internal static List<string> DoGetPortNames()
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
