// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Linq;
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
            IsDevicesEnumerationComplete = (from p in _ports
                                            where p.IsDevicesEnumerationComplete
                                            select p).Any();
            if (IsDevicesEnumerationComplete)
            {
                DeviceEnumerationCompleted?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <inheritdoc/>
        /// <exception cref="NotImplementedException">This API is not available in </exception>
        public override NanoDeviceBase AddDevice(string deviceId)
        {
            // None of the Port*Manager has a check whether deviceId matches the ID handled by the manager.
            throw new NotImplementedException();
            //_ports.ForEach(p =>
            //{
            //    p.AddDevice(deviceId);
            //});
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
