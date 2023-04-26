//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Threading.Tasks;


namespace nanoFramework.Tools.Debugger.PortComposite
{
    public class PortCompositeDeviceManager : PortBase
    {
        private readonly List<PortBase> _ports = new List<PortBase>();
        public override event EventHandler DeviceEnumerationCompleted;
        public override event EventHandler<StringEventArgs> LogMessageAvailable;

        public PortCompositeDeviceManager(
            IEnumerable<PortBase> ports,
            bool startDeviceWatchers = true)
        {
            NanoFrameworkDevices = NanoFrameworkDevices.Instance;

            _ports.AddRange(ports);

            SubscribeToPortEvents();


            Task.Factory.StartNew(() =>
            {
                if (startDeviceWatchers)
                {
                    _ports.ForEach(p => p.StartDeviceWatchers());
                }
            });
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
            DeviceEnumerationCompleted?.Invoke(this, EventArgs.Empty);
        }

        /// <inheritdoc/>
        /// <exception cref="NotImplementedException">This API is not available in </exception>
        public override void AddDevice(string deviceId)
        {
            _ports.ForEach(p =>
            {
                p.AddDevice(deviceId);
            });
        }

        public override void StartDeviceWatchers()
        {
            _ports.ForEach(p => p.StartDeviceWatchers());
        }

        public override void StopDeviceWatchers()
        {
            _ports.ForEach(p => p.StopDeviceWatchers());
        }

        public override void ReScanDevices()
        {
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
