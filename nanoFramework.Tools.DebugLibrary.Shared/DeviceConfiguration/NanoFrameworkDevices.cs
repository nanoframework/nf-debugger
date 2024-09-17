// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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
            get
            {
                lock (typeof(NanoFrameworkDevices))
                {
                    return _instance.Value;
                }
            }
        }

        private NanoFrameworkDevices() { }
    }
}
