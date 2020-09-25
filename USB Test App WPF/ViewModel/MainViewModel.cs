//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using GalaSoft.MvvmLight;
using nanoFramework.ANT.Services.NanoFrameworkService;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.WireProtocol;
using PropertyChanged;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Threading;

namespace Serial_Test_App_WPF.ViewModel
{
    /// <summary>
    /// This class contains properties that the main View can data bind to.
    /// <para>
    /// Use the <strong>mvvminpc</strong> snippet to add bindable properties to this ViewModel.
    /// </para>
    /// <para>
    /// You can also use Blend to data bind with the tool's support.
    /// </para>
    /// <para>
    /// See http://www.galasoft.ch/mvvm
    /// </para>
    /// </summary>
    [AddINotifyPropertyChangedInterface]
    public class MainViewModel : ViewModelBase

    {
        /// <summary>
        /// Initializes a new instance of the MainViewModel class.
        /// </summary>
        public MainViewModel()
        {
            ////if (IsInDesignMode)
            ////{
            ////    // Code runs in Blend --> create design time data.
            ////}
            ////else
            ////{
            ////    // Code runs "for real"
            ////}
        }

        public INFSerialDebugClientService SerialDebugService { get; set; }

        public void OnSerialDebugServiceChanged()
        {
            if (SerialDebugService != null)
            {
                SerialDebugService.SerialDebugClient.DeviceEnumerationCompleted += SerialDebugClient_DeviceEnumerationCompleted;

                SerialDebugService.SerialDebugClient.LogMessageAvailable += SerialDebugClient_LogMessageAvailable;
            }
        }

        private void SerialDebugClient_LogMessageAvailable(object sender, StringEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                Debug.WriteLine(e.EventText);
            }));
        }

        private void SerialDebugClient_DeviceEnumerationCompleted(object sender, EventArgs e)
        {
            SerialDebugService.SerialDebugClient.DeviceEnumerationCompleted -= SerialDebugClient_DeviceEnumerationCompleted;
            //WindowWrapper.Current().Dispatcher.Dispatch(() =>
            //{
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                SelectedTransportType = TransportType.Serial;
                UpdateAvailableDevices();
            }));
            //});
        }

        public ObservableCollection<NanoDeviceBase> AvailableDevices { get; set; }

        private void UpdateAvailableDevices()
        {
            switch (SelectedTransportType)
            {
                case TransportType.Serial:

                    Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                        //BusySrv.ShowBusy(Res.GetString("HC_Searching"));
                        AvailableDevices = new ObservableCollection<NanoDeviceBase>(SerialDebugService.SerialDebugClient.NanoFrameworkDevices);
                                SerialDebugService.SerialDebugClient.NanoFrameworkDevices.CollectionChanged += NanoFrameworkDevices_CollectionChanged;
                                // if there's just one, select it
                                SelectedDevice = (AvailableDevices.Count == 1) ? AvailableDevices.First() : null;
                            //BusySrv.HideBusy();
                    }));
                    break;

                case TransportType.Usb:

                    //WindowWrapper.Current().Dispatcher.Dispatch(() =>
                    //{
                        //BusySrv.ShowBusy(Res.GetString("HC_Searching"));
                        //AvailableDevices = new ObservableCollection<NanoDeviceBase>(UsbDebugService.UsbDebugClient.NanoFrameworkDevices);
                        //UsbDebugService.UsbDebugClient.NanoFrameworkDevices.CollectionChanged += NanoFrameworkDevices_CollectionChanged;
                        // if there's just one, select it
                        //SelectedDevice = (AvailableDevices.Count == 1) ? AvailableDevices.First() : null;
                        //BusySrv.HideBusy();
                    //});

                    break;

                case TransportType.TcpIp:
                    // TODO
                    //BusySrv.ShowBusy("Not implemented yet! Why not give it a try??");
                    //await Task.Delay(2500);
                    //await WindowWrapper.Current().Dispatcher.DispatchAsync(() =>
                    //{
                    //    AvailableDevices = new ObservableCollection<NanoDeviceBase>();
                    //    SelectedDevice = null;
                    //});
                    //BusySrv.HideBusy();
                    break;
            }
        }

        private void NanoFrameworkDevices_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                // handle this according to the selected device type 
                switch (SelectedTransportType)
                {
                    //case TransportType.Usb:
                    //    AvailableDevices = new ObservableCollection<NanoDeviceBase>(UsbDebugService.UsbDebugClient.NanoFrameworkDevices);
                    //    break;

                    case TransportType.Serial:
                        AvailableDevices = new ObservableCollection<NanoDeviceBase>(SerialDebugService.SerialDebugClient.NanoFrameworkDevices);
                        break;

                    default:
                        // shouldn't get here...
                        break;
                }

                // if there's just one, select it
                SelectedDevice = (AvailableDevices.Count == 1) ? AvailableDevices.First() : null;

            }));
        }

        public NanoDeviceBase SelectedDevice { get; set; }


        #region Transport
        public List<TransportType> AvailableTransportTypes { get; set; }

        public TransportType SelectedTransportType { get; set; }

        public void OnSelectedTransportTypeChanged()
        {
            UpdateAvailableDevices();
        }
        #endregion
    }
}