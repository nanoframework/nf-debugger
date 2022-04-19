using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    public class DeviceWatcher : IDisposable
    {
        private bool _started = false;
        private List<string> _ports;
        private Thread _threadWatch = null;
        private readonly PortSerialManager _ownerManager;

        public delegate void EventDeviceAdded(object sender, string port);

        public event EventDeviceAdded Added;

        public delegate void EventDeviceRemoved(object sender, string port);

        public event EventDeviceRemoved Removed;

        public DeviceWatcherStatus Status { get; internal set; }

        /// <summary>
        /// Constructor for a <see cref="PortSerialManager"/> device watcher class.
        /// </summary>
        /// <param name="owner">The <see cref="PortSerialManager"/> that owns this device watcher.</param>
        public DeviceWatcher(PortSerialManager owner)
        {
            _ownerManager = owner;
        }

        public void Start()
        {
            if (!_started)
            {
                _threadWatch = new Thread(() =>
                {
                    _ownerManager.OnLogMessageAvailable($"PortSerial device watcher started @ Thread {_threadWatch.ManagedThreadId} [ProcessID: {Process.GetCurrentProcess().Id}]");

                    _ports = new List<string>();

                    _started = true;

                    Status = DeviceWatcherStatus.Started;

                    while (_started)
                    {
                        try
                        {
                            var ports = GetPortNames();

                            // process ports that have arrived
                            foreach (var port in ports)
                            {
                                if (!_ports.Contains(port))
                                {
                                    _ports.Add(port);
                                    Added?.Invoke(this, port);
                                }
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
                })
                {
                    IsBackground = true,
                    Priority = ThreadPriority.Lowest
                };

                _threadWatch.Start();
            }
        }

        public List<string> GetPortNames()
        {

            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? GetPortNames_Linux()
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? GetPortNames_OSX()
                : RuntimeInformation.IsOSPlatform(OSPlatform.Create("FREEBSD")) ? GetPortNames_FreeBSD()
                : GetPortNames_Windows();
        }

        private List<string> GetPortNames_Linux()
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

        private List<string> GetPortNames_OSX()
        {
            List<string> ports = new List<string>();

            foreach (string name in Directory.GetFiles("/dev", "tty.usbserial*"))
            {
                // GetFiles can return unexpected results because of 8.3 matching.
                // Like /dev/tty
                if (name.StartsWith("/dev/tty.", StringComparison.Ordinal))
                {
                    ports.Add(name);
                }
            }

            foreach (string name in Directory.GetFiles("/dev", "cu.usbserial*"))
            {
                if (name.StartsWith("/dev/cu.", StringComparison.Ordinal))
                {
                    ports.Add(name);
                }
            }

            return ports;
        }

        private List<string> GetPortNames_FreeBSD()
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

        private List<string> GetPortNames_Windows()
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
                                        if (portName != null)
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
                                    // Get the full qualified name of the device
                                    string deviceFullPath = (string)deviceFullPaths.GetValue(portName);
                                    if (deviceFullPath != null)
                                    {
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

        public void Stop()
        {
            _started = false;
            Status = DeviceWatcherStatus.Stopping;
        }

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
