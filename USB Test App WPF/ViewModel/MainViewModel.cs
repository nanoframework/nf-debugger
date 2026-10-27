//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.ANT.Services.NanoFrameworkService;
using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.WireProtocol;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;

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
    public class MainViewModel : ObservableObject
    {
        private INFSerialDebugClientService _serialDebugService;
        private ObservableCollection<NanoDeviceBase> _availableDevices = new ObservableCollection<NanoDeviceBase>();
        private NanoDeviceBase _selectedDevice;
        private List<TransportType> _availableTransportTypes;
        private TransportType _selectedTransportType;

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
            get => _serialDebugService;
            set
            {
                if (SetProperty(ref _serialDebugService, value))
                {
                    SelectedTransportType = TransportType.Serial;

                    NanoDeviceCollection devices = _serialDebugService.SerialDebugClient.NanoFrameworkDevices;

                    // subscribe before loading the current devices, so no change is missed
                    devices.DeviceAdded += NanoFrameworkDevices_DeviceAdded;
                    devices.DeviceRemoved += NanoFrameworkDevices_DeviceRemoved;

                    AvailableDevices = new ObservableCollection<NanoDeviceBase>(devices);

                    SerialDebugService.SerialDebugClient.LogMessageAvailable += SerialDebugClient_LogMessageAvailable1;
                }
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
                // catch all as the dispatcher is not always available and that's OK
            }
        }

        private void SerialDebugClient_LogMessageAvailable(object sender, StringEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                Debug.WriteLine(e.EventText);
            }));
        }

        public ObservableCollection<NanoDeviceBase> AvailableDevices
        {
            get => _availableDevices;
            set => SetProperty(ref _availableDevices, value);
        }

        // the library raises these on its own threads: the device list is only changed on the UI thread
        private void NanoFrameworkDevices_DeviceAdded(object sender, NanoDeviceEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                // the device may already be there, if it was added while loading the initial list
                if (!AvailableDevices.Contains(e.Device))
                {
                    AvailableDevices.Add(e.Device);
                }

                UpdateSelectedDevice();
            }));
        }

        private void NanoFrameworkDevices_DeviceRemoved(object sender, NanoDeviceEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                AvailableDevices.Remove(e.Device);

                UpdateSelectedDevice();
            }));
        }

        private void UpdateSelectedDevice()
        {
            // if there's just one, select it
            SelectedDevice = (AvailableDevices.Count == 1) ? AvailableDevices.First() : null;
        }

        public NanoDeviceBase SelectedDevice
        {
            get => _selectedDevice;
            set => SetProperty(ref _selectedDevice, value);
        }

        #region Transport
        public List<TransportType> AvailableTransportTypes
        {
            get => _availableTransportTypes;
            set => SetProperty(ref _availableTransportTypes, value);
        }

        public TransportType SelectedTransportType
        {
            get => _selectedTransportType;
            set => SetProperty(ref _selectedTransportType, value);
        }
        #endregion
    }
}
