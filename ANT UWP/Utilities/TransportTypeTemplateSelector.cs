//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using NanoFramework.Tools.Debugger.WireProtocol;

namespace NanoFramework.ANT.Utilities
{
    public class TransportTypeTemplateSelector : DataTemplateSelector
    {
        public DataTemplate SerialTemplate { get; set; }
        public DataTemplate UsbTemplate { get; set; }
        public DataTemplate TcpIpTemplate { get; set; }

        protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        {
            if (item != null)
            {
                TransportType tType = (TransportType)item;
                switch (tType)
                {
                    case TransportType.Serial:
                        return SerialTemplate;

                    case TransportType.Usb:
                        return UsbTemplate;

                    case TransportType.TcpIp:
                        return TcpIpTemplate;
                }
            }
            return base.SelectTemplateCore(item, container);
        }
    }
}
