// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;


namespace nanoFramework.Tools.Debugger.PortComposite
{
    public class PortCompositeDeviceManager : PortBase
    {
        private readonly List<PortBase> _ports = new List<PortBase>();
        private readonly object _lifecycleLock = new object();
        private bool _disposed = false;
        public override event EventHandler DeviceEnumerationCompleted;
        public override event EventHandler<StringEventArgs> LogMessageAvailable;

        /// <summary>
        /// Creates a device manager that aggregates several ports.
        /// </summary>
        /// <param name="ports">The ports to aggregate. They are owned by this manager and disposed with it.</param>
        /// <param name="startDeviceWatchers">Indicates whether to start the device watchers.</param>
        public PortCompositeDeviceManager(
            IEnumerable<PortBase> ports,
            bool startDeviceWatchers = true)
        {
            _ports.AddRange(ports);

            SubscribeToPortEvents();


            Task.Factory.StartNew(() =>
            {
                if (startDeviceWatchers)
                {
                    lock (_lifecycleLock)
                    {
                        if (!_disposed)
                        {
                            _ports.ForEach(p => p.StartDeviceWatchers());
                        }
                    }
                }
            });
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
            }

            List<Exception> exceptions = null;

            if (disposing)
            {
                // dispose all ports, even if one of them throws
                foreach (var port in _ports)
                {
                    port.DeviceEnumerationCompleted -= OnPortDeviceEnumerationCompleted;
                    port.LogMessageAvailable -= OnLogMessageAvailable;

                    try
                    {
                        port.Dispose();
                    }
                    catch (Exception ex)
                    {
                        (exceptions ??= new List<Exception>()).Add(ex);
                    }
                }
            }

            base.Dispose(disposing);

            if (exceptions?.Count == 1)
            {
                ExceptionDispatchInfo.Capture(exceptions[0]).Throw();
            }
            else if (exceptions is not null)
            {
                throw new AggregateException(exceptions);
            }
        }

        private void SubscribeToPortEvents()
        {
            _ports.ForEach(p =>
            {
                p.DeviceEnumerationCompleted += OnPortDeviceEnumerationCompleted;
                p.LogMessageAvailable += OnLogMessageAvailable;
            });
        }

        private void OnLogMessageAvailable(object sender, StringEventArgs e)
        {
            LogMessageAvailable?.Invoke(this, new StringEventArgs(e.EventText));
        }

        private void OnPortDeviceEnumerationCompleted(object sender, EventArgs e)
        {
            IsDevicesEnumerationComplete = (from p in _ports
                                            where p.IsDevicesEnumerationComplete
                                            select p).Any();
            if (IsDevicesEnumerationComplete)
            {
                DeviceEnumerationCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <inheritdoc/>
        /// <exception cref="NotImplementedException">This API is not available in PortCompositeDeviceManager.</exception>
        public override NanoDeviceBase AddDevice(string deviceId)
        {
            // None of the Port*Manager has a check whether deviceId matches the ID handled by the manager,
            // so we don't know how to add a device here.
            throw new NotImplementedException();
        }

        public override void StartDeviceWatchers()
        {
            IsDevicesEnumerationComplete = false;
            _ports.ForEach(p => p.StartDeviceWatchers());
        }

        public override void StopDeviceWatchers()
        {
            _ports.ForEach(p => p.StopDeviceWatchers());
        }

        public override void ReScanDevices()
        {
            IsDevicesEnumerationComplete = false;
            Task.Run(() =>
            {
                _ports.ForEach(p => p.ReScanDevices());
            });
        }

        public override void DisposeDevice(string instanceId)
        {
            _ports.ForEach(p => p.DisposeDevice(instanceId));
        }
    }
}
