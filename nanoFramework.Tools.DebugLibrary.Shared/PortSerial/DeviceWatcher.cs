using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
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
            List<string> portNames = new List<string>();
            RegistryKey activePorts = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\usbser\Enum");
            if (activePorts != null)
            {
                var numberPorts = (int)activePorts.GetValue("Count");

                for (int i = 0; i < numberPorts; i++)
                {
                    string portDescription = (string)activePorts.GetValue($"{i}");
                    if (portDescription != null)
                    {
                        RegistryKey portKeyInfo = Registry.LocalMachine.OpenSubKey($"SYSTEM\\CurrentControlSet\\Enum\\{portDescription}\\Device Parameters");
                        if (portKeyInfo != null)
                        {
                            string portName = (string)portKeyInfo.GetValue($"PortName");
                            if (portName != null)
                            {
                                portNames.Add(portName);
                            }
                        }
                    }
                }
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
