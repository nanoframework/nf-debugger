//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
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
            NanoFrameworkDevices = new ObservableCollection<NanoDeviceBase>();

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
                p.NanoFrameworkDevices.CollectionChanged += OnPortNanoFrameworkDevicesOnCollectionChanged;
                p.LogMessageAvailable += OnLogMessageAvailable;
            });
        }

        private void OnLogMessageAvailable(object sender, StringEventArgs e)
        {
            LogMessageAvailable?.Invoke(this, new StringEventArgs(e.EventText));
        }

        private void OnPortNanoFrameworkDevicesOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Reset:
                    NanoFrameworkDevices.Clear();
                    break;

                case NotifyCollectionChangedAction.Add:
                    e.NewItems.Cast<NanoDeviceBase>().ToList().ForEach(d => NanoFrameworkDevices.Add(d));
                    break;

                case NotifyCollectionChangedAction.Remove:
                    e.OldItems.Cast<NanoDeviceBase>().ToList().ForEach(d => NanoFrameworkDevices.Remove(d));
                    break;
            }
        }

        private void OnPortDeviceEnumerationCompleted(object sender, EventArgs e)
        {
            DeviceEnumerationCompleted?.Invoke(this, EventArgs.Empty);
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
