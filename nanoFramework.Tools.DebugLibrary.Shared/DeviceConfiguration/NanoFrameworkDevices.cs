using System;
using System.Collections.ObjectModel;

namespace nanoFramework.Tools.Debugger
{
    public class NanoFrameworkDevices : ObservableCollection<NanoDeviceBase>
    {
        private static readonly Lazy<NanoFrameworkDevices> _instance =
        new Lazy<NanoFrameworkDevices>(() => new NanoFrameworkDevices());

        public static NanoFrameworkDevices Instance
        {
            get { return _instance.Value; }
        }

        private NanoFrameworkDevices() { }
    }
}
