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
        private readonly List<string> _ports = new List<string>();

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
                    _started = true;

                    Status = DeviceWatcherStatus.Started;
                    
                    while (_started)
                    {
                        var ports = SerialPort.GetPortNames().Distinct();

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

        public void Stop()
        {
            _started = false;
            Status = DeviceWatcherStatus.Stopping;
        }
    }
}
