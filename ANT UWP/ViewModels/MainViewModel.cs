using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MFDeploy.Models;
using MFDeploy.Services.BusyService;
using MFDeploy.Services.Dialog;
using MFDeploy.Services.NetMicroFrameworkService;
using MFDeploy.Utilities;
using Microsoft.NetMicroFramework.Tools;
using Microsoft.NetMicroFramework.Tools.MFDeployTool.Engine;
using Microsoft.SPOT.Debugger.WireProtocol;

namespace MFDeploy.ViewModels
{
    public class MainViewModel : MyViewModelBase
    {

        public MainViewModel(IMyDialogService dlg, IBusyService busy)
        {
            this.DialogSrv = dlg;
            this.BusySrv = busy;

            AvailableTransportTypes = EnumHelper.ListOf<TransportType>();           
            AvailableDevices = new ObservableCollection<MFDeviceBase>();

            SelectedDevice = null;
            SelectedDeviceConnectionResult = PingConnectionResult.None;
            IsBusyHeader = false;

        }
        public INetMFUsbDebugClientService UsbDebugService { get; set; } = null;
        public void OnUsbDebugServiceChanged()
        {
            if (UsbDebugService != null)
            {
                UsbDebugService.UsbDebugClient.DeviceEnumerationCompleted += UsbDebugClient_DeviceEnumerationCompleted;                
            }
        }

        private void UsbDebugClient_DeviceEnumerationCompleted(object sender, EventArgs e)
        {
            UsbDebugService.UsbDebugClient.DeviceEnumerationCompleted -= UsbDebugClient_DeviceEnumerationCompleted;
            SelectedTransportType = TransportType.Usb;
        }

        public string PageHeader { get; set; }
        public bool IsBusyHeader { get; set; }
      
        public ObservableCollection<MFDeviceBase> AvailableDevices { get; set; }

        private void UpdateAvailableDevices()
        {            
            switch (SelectedTransportType)
            {
                case TransportType.Serial:
                    // TODO
                    BusySrv.ShowBusy("Searching Serial...");
                    BusySrv.HideBusy();
                    break;
                case TransportType.Usb:
                    // need to implement type conversion from MFDevices to MFDeviceBase to be able to copy reference,
                    // so changes in UsbDebugService.UsbDebugClient.MFDevices get reflected in AvailableDevices
                    AvailableDevices = new ObservableCollection<MFDeviceBase>(UsbDebugService.UsbDebugClient.MFDevices);
                    break;
                case TransportType.TcpIp:
                    // TODO
                    BusySrv.ShowBusy("Searching TcpIp...");
                    BusySrv.HideBusy();
                    break;

            }
        }

        public MFDeviceBase SelectedDevice { get; set; }

        public async void OnSelectedDeviceChanged()
        {
            SelectedDeviceConnectionResult = PingConnectionResult.None;

            // try to connect
            await SelectedDeviceConnect();

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

        public async void SelectedDevicePing()
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

        public bool ConnectAvailable { get { return (ConnectionStateResult == ConnectionState.ConnectAvailable); } }
        public bool DisconnectAvailable { get { return (ConnectionStateResult == ConnectionState.DisconnectAvailable); } }
        public bool Connecting { get { return (ConnectionStateResult == ConnectionState.Connecting); } }
        public bool Disconnecting { get { return (ConnectionStateResult == ConnectionState.Disconnecting); } }

        public async void ConnectDisconnect()
        {
            IsBusyHeader = true;
            
            if (ConnectionStateResult == ConnectionState.ConnectAvailable)
            {
                await SelectedDeviceConnect();
            }
            else
            {
                SelectedDeviceDisconnect();
            }

            IsBusyHeader = false;
        }

        private async Task SelectedDeviceConnect()
        {            
            ConnectionStateResult = ConnectionState.Connecting;
            bool connectOk = await SelectedDevice.DebugEngine.ConnectAsync(3, 1000);

            ConnectionStateResult = connectOk ? ConnectionState.DisconnectAvailable : ConnectionState.ConnectAvailable;
            if (!connectOk)
            {
                await DialogSrv.ShowMessageAsync(Res.GetString("HC_ConnectionError"));
            }
        }

        private void SelectedDeviceDisconnect()
        {
            ConnectionStateResult = ConnectionState.Disconnecting;
            SelectedDevice.DebugEngine.Disconnect();
            ConnectionStateResult = ConnectionState.ConnectAvailable;
        }

        #endregion
    }

}
