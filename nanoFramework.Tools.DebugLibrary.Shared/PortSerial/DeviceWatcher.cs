using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    public class DeviceWatcher
    {
        private bool _started = false;
        private List<string> _ports;

        public delegate void EventDeviceAdded(object sender, string port);

        public event EventDeviceAdded Added;

        public delegate void EventDeviceRemoved(object sender, string port);

        public event EventDeviceRemoved Removed;

        public DeviceWatcherStatus Status { get; internal set; }

        public void Start()
        {
            if (!_started)
            {
                var threadWatch = new Thread(() =>
                {
                    _ports = new List<string>();

                    _started = true;

                    Status = DeviceWatcherStatus.Started;
                    
                    while (_started)
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

                    Status = DeviceWatcherStatus.Stopped;
                })
                {
                    Priority = ThreadPriority.Lowest
                };
                threadWatch.Start();
            }
        }

        private List<string> GetPortNames()
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
                                            RegistryKey device = Registry.LocalMachine.OpenSubKey($"SYSTEM\\CurrentControlSet\\Enum\\{devicePathDetail.Groups[1]}\\{devicePathDetail.Groups[2]}\\{devicePathDetail.Groups[3]}");
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
    }
}
