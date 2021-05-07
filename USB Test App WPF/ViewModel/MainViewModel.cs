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
using System.Windows.Data;
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
        private readonly object _lockObj = new object();

        INFSerialDebugClientService _serialDebugService;

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

        public INFSerialDebugClientService SerialDebugService 
        { 
            get
            {
                return _serialDebugService;
            }

            set
            {
                if (_serialDebugService == value)
                {
                    return;
                }

                _serialDebugService = value;

                RaisePropertyChanged("SerialDebugService");

                SelectedTransportType = TransportType.Serial;

                AvailableDevices = _serialDebugService.SerialDebugClient.NanoFrameworkDevices;

                // need to do this in order to allow sync from another thread
                BindingOperations.EnableCollectionSynchronization(AvailableDevices, _lockObj);

                SerialDebugService.SerialDebugClient.NanoFrameworkDevices.CollectionChanged += NanoFrameworkDevices_CollectionChanged;

                SerialDebugService.SerialDebugClient.LogMessageAvailable += SerialDebugClient_LogMessageAvailable1;
            }
        }

        private void SerialDebugClient_LogMessageAvailable1(object sender, StringEventArgs e)
        {
            try
            {
                Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    Debug.WriteLine(e.EventText);
                }));
            }
            catch
            {
                // catch all as the dispatcher is ont always available and that's OK
            }
        }

        private void SerialDebugClient_LogMessageAvailable(object sender, StringEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                Debug.WriteLine(e.EventText);
            }));
        }

        public ObservableCollection<NanoDeviceBase> AvailableDevices { get; set; }

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

        #endregion
    }
}