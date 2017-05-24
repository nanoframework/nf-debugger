//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using GalaSoft.MvvmLight.Messaging;
using NanoFramework.ANT.Models;
using NanoFramework.ANT.Services.BusyService;
using NanoFramework.ANT.Services.Dialog;
using NanoFramework.ANT.Services.NanoFrameworkService;
using NanoFramework.ANT.Services.SettingsServices;
using NanoFramework.ANT.Utilities;
using NanoFramework.Tools.Debugger;
using NanoFramework.Tools.Debugger.WireProtocol;
using PropertyChanging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Template10.Common;

namespace NanoFramework.ANT.ViewModels
{
    //[ImplementPropertyChanging]
    public class MainViewModel : MyViewModelBase, INotifyPropertyChanging
    {
        // messaging tokens
        public const int WRITE_TO_OUTPUT_TOKEN = 1;
        public const int SELECTED_NULL_TOKEN = 2;

        private IAppSettingsService settingsSrv;

        // keep this here otherwise Fody won't be able to properly implement INotifyPropertyChanging
        #pragma warning disable 67
        public event PropertyChangingEventHandler PropertyChanging;
        #pragma warning restore 67

        public MainViewModel(IMyDialogService dlg, IBusyService busy, IAppSettingsService settings)
        {
            this.DialogSrv = dlg;
            this.BusySrv = busy;
            this.settingsSrv = settings;

            AvailableTransportTypes = EnumHelper.ListOf<TransportType>();           
            AvailableDevices = new ObservableCollection<NanoDeviceBase>();

            SelectedDevice = null;
            SelectedDeviceConnectionResult = PingConnectionResult.None;
            IsBusyHeader = false;
        }

        public INFUsbDebugClientService UsbDebugService { get; set; } = null;
        public void OnUsbDebugServiceChanged()
        {
            if (UsbDebugService != null)
            {
                UsbDebugService.UsbDebugClient.DeviceEnumerationCompleted += UsbDebugClient_DeviceEnumerationCompleted;                
            }
        }

        public INFSerialDebugClientService SerialDebugService { get; set; } = null;
        public void OnSerialDebugServiceChanged()
        {
            if (SerialDebugService != null)
            {
                SerialDebugService.SerialDebugClient.DeviceEnumerationCompleted += SerialDebugClient_DeviceEnumerationCompleted;
            }
        }

        private void UsbDebugClient_DeviceEnumerationCompleted(object sender, EventArgs e)
        {
            UsbDebugService.UsbDebugClient.DeviceEnumerationCompleted -= UsbDebugClient_DeviceEnumerationCompleted;            
            WindowWrapper.Current().Dispatcher.Dispatch(() =>
            {
                SelectedTransportType = TransportType.Usb;
            });
        }

        private void SerialDebugClient_DeviceEnumerationCompleted(object sender, EventArgs e)
        {
            SerialDebugService.SerialDebugClient.DeviceEnumerationCompleted -= SerialDebugClient_DeviceEnumerationCompleted;
            WindowWrapper.Current().Dispatcher.Dispatch(() =>
            {
                SelectedTransportType = TransportType.Serial;
            });
        }

        public string PageHeader { get; set; }
        public bool IsBusyHeader { get; set; }
      
        public ObservableCollection<NanoDeviceBase> AvailableDevices { get; set; }

        private void UpdateAvailableDevices()
        {            
            switch (SelectedTransportType)
            {
                case TransportType.Serial:
                    
                    WindowWrapper.Current().Dispatcher.Dispatch(() =>
                    {
                        BusySrv.ShowBusy(Res.GetString("HC_Searching"));
                        AvailableDevices = new ObservableCollection<NanoDeviceBase>(SerialDebugService.SerialDebugClient.NanoFrameworkDevices);
                        SerialDebugService.SerialDebugClient.NanoFrameworkDevices.CollectionChanged += NanoFrameworkDevices_CollectionChanged;
                        // if there's just one, select it
                        SelectedDevice = (AvailableDevices.Count == 1) ? AvailableDevices.First() : null;
                        BusySrv.HideBusy();
                    });
                    break;

                case TransportType.Usb:
                   
                    WindowWrapper.Current().Dispatcher.Dispatch(() =>
                    {
                        BusySrv.ShowBusy(Res.GetString("HC_Searching"));
                        AvailableDevices = new ObservableCollection<NanoDeviceBase>(UsbDebugService.UsbDebugClient.NanoFrameworkDevices);
                        UsbDebugService.UsbDebugClient.NanoFrameworkDevices.CollectionChanged += NanoFrameworkDevices_CollectionChanged;
                        // if there's just one, select it
                        SelectedDevice = (AvailableDevices.Count == 1) ? AvailableDevices.First() : null;
                        BusySrv.HideBusy();
                    });
                   
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

        private  void NanoFrameworkDevices_CollectionChanged(object sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            WindowWrapper.Current().Dispatcher.Dispatch(() =>
            {
                // handle this according to the selected device type 
                switch(SelectedTransportType)
                {
                    case TransportType.Usb:
                        AvailableDevices = new ObservableCollection<NanoDeviceBase>(UsbDebugService.UsbDebugClient.NanoFrameworkDevices);
                        break;

                    case TransportType.Serial:
                        AvailableDevices = new ObservableCollection<NanoDeviceBase>(SerialDebugService.SerialDebugClient.NanoFrameworkDevices);
                        break;

                    default:
                        // shouldn't get here...
                        break;
                }

                // if there's just one, select it
                SelectedDevice = (AvailableDevices.Count == 1) ? AvailableDevices.First() : null;

            });
        }

        public NanoDeviceBase SelectedDevice { get; set; }

        public void OnSelectedDeviceChanging()
        {
            Debug.WriteLine($"Selected device changing from {SelectedDevice?.Description}");

            // disconnect device becoming unselected
            SelectedDeviceDisconnect();
        }

        public void OnSelectedDeviceChanged()
        {
            Debug.WriteLine($"Selected device changed to {SelectedDevice.Description}");

            SelectedDeviceConnectionResult = PingConnectionResult.None;

            if (SelectedDevice != null)
            {
                SelectedDevice.DebugEngine.SpuriousCharactersReceived -= DebugEngine_SpuriousCharactersReceived;
                SelectedDevice.DebugEngine.SpuriousCharactersReceived += DebugEngine_SpuriousCharactersReceived;
            }
            else
            {
                this.MessengerInstance.Send<NotificationMessage>(new NotificationMessage(""), SELECTED_NULL_TOKEN);
            }

            // try to connect
            Task t = SelectedDeviceConnect();
        }

        private void DebugEngine_SpuriousCharactersReceived(object sender, NanoFramework.Tools.Debugger.StringEventArgs e)
        {
            string textToSend = settingsSrv.AddTimestampToOutput ? $"[{DateTime.Now.ToString()}] {e.EventText}" : e.EventText;
            this.MessengerInstance.Send<NotificationMessage>(new NotificationMessage(textToSend), WRITE_TO_OUTPUT_TOKEN);
        }

        public string SelectedDeviceDisplayContent
        {
            get
            {
                if(SelectedDevice != null)
                {
                    return SelectedDevice.Description;
                }
                else
                {
                    return ((AvailableDevices?.Count > 0) ? Res.GetString("HC_SelectADevice") : Res.GetString("HC_NoDevices"));
                }
            }
        }

        #region Transport
        public List<TransportType> AvailableTransportTypes { get; set; }
        public TransportType SelectedTransportType { get; set; }

        public void OnSelectedTransportTypeChanged()
        {
            UpdateAvailableDevices();
        }
        #endregion

        #region ping
        public PingConnectionResult SelectedDeviceConnectionResult { get; set; }
        public bool ConnectionResultOk { get { return (SelectedDeviceConnectionResult == PingConnectionResult.Ok); } }
        public bool ConnectionResultError { get { return (SelectedDeviceConnectionResult == PingConnectionResult.Error); } }
        public bool Pinging { get { return (SelectedDeviceConnectionResult == PingConnectionResult.Busy); } }

        public async Task SelectedDevicePing()
        {
            IsBusyHeader = true;
            
            SelectedDeviceConnectionResult = PingConnectionResult.Busy;
            try
            {
                PingConnectionType connection = await SelectedDevice.PingAsync();
                SelectedDeviceConnectionResult = (connection != PingConnectionType.NoConnection) ? PingConnectionResult.Ok : PingConnectionResult.Error;
            }
            catch
            {
                SelectedDeviceConnectionResult = PingConnectionResult.Error;
            }
            finally
            {
                IsBusyHeader = false;
            }
        }

        #endregion

        #region connect / disconnect
        public ConnectionState ConnectionStateResult { get; set; } = ConnectionState.None;

        public bool Connected { get { return (ConnectionStateResult == ConnectionState.Connected); } }
        public bool Disconnected { get { return (ConnectionStateResult == ConnectionState.Disconnected); } }
        public bool Connecting { get { return (ConnectionStateResult == ConnectionState.Connecting); } }
        public bool Disconnecting { get { return (ConnectionStateResult == ConnectionState.Disconnecting); } }

        public async Task ConnectDisconnect()
        {
            if (ConnectionStateResult == ConnectionState.Connected)
            {
                SelectedDeviceDisconnect();
            }
            else
            {
                await SelectedDeviceConnect();
            }
        }

        private async Task SelectedDeviceConnect()
        {
            if (SelectedDevice != null)
            {
                await WindowWrapper.Current().Dispatcher.DispatchAsync(() =>
                {
                    IsBusyHeader = true;
                    ConnectionStateResult = ConnectionState.Connecting;
                });

                bool connectOk = await SelectedDevice.DebugEngine.ConnectAsync(3, 1000);

                await WindowWrapper.Current().Dispatcher.DispatchAsync(() =>
                {
                    ConnectionStateResult = connectOk ? ConnectionState.Connected : ConnectionState.Disconnected;
                    IsBusyHeader = false;
                });
                if (!connectOk)
                {
                    await DialogSrv.ShowMessageAsync(Res.GetString("HC_ConnectionError"));
                }
            }
        }

        private void SelectedDeviceDisconnect()
        {
            if (SelectedDevice != null)
            {
                WindowWrapper.Current().Dispatcher.Dispatch(() =>
                {
                    IsBusyHeader = true;
                    ConnectionStateResult = ConnectionState.Disconnecting;
                });

                SelectedDevice.DebugEngine.Disconnect();

                WindowWrapper.Current().Dispatcher.Dispatch(() =>
                {
                    ConnectionStateResult = ConnectionState.Disconnected;
                    IsBusyHeader = false;
                });
            }                      
        }

        #endregion


    }

}
