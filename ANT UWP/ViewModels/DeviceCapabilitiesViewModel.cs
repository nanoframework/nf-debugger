//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using NanoFramework.ANT.Services.BusyService;
using NanoFramework.ANT.Services.Dialog;
using NanoFramework.ANT.Utilities;
using Microsoft.Practices.ServiceLocation;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Template10.Services.NavigationService;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI.Xaml.Navigation;
using NanoFramework.ANT.Views.Config;
using GalaSoft.MvvmLight.Messaging;
using NanoFramework.Tools.Debugger;
using NanoFramework.Tools.Debugger.Extensions;

namespace NanoFramework.ANT.ViewModels
{
    public class DeviceCapabilitiesViewModel : MyViewModelBase
    {
        //private instance of Main to get general stuff
        private MainViewModel MainVM { get { return ServiceLocator.Current.GetInstance<MainViewModel>(); } }

        public DeviceCapabilitiesViewModel(IMyDialogService dlg, IBusyService busy)
        {
            this.DialogSrv = dlg;
            this.BusySrv = busy;
        }

        #region Navigation
        public override async Task OnNavigatedToAsync(object parameter, NavigationMode mode, IDictionary<string, object> suspensionState)
        {

            if (suspensionState.Any())
            {
                //Value = suspensionState[nameof(Value)]?.ToString();
            }
            await Task.CompletedTask;

            MainVM.PageHeader = Res.GetString("DC_PageHeader");
            MessengerInstance.Register<NotificationMessage>(this, MainViewModel.SELECTED_NULL_TOKEN, (message) => SelectedIsNullHandler());

            // load device info
            await LoadDeviceInfo();
        }

        public override async Task OnNavigatedFromAsync(IDictionary<string, object> suspensionState, bool suspending)
        {
            if (suspending)
            {
                //suspensionState[nameof(Value)] = Value;
            }
            MessengerInstance.Unregister(this);
            await Task.CompletedTask;
        }



        public override async Task OnNavigatingFromAsync(NavigatingEventArgs args)
        {
            args.Cancel = false;
            await Task.CompletedTask;
        }

        #endregion    }

        private void SelectedIsNullHandler()
        {
            this.NavigationService.Navigate(Pages.MainPage);
        }

        public StringBuilder DeviceDeploymentMap { get; set; }

        public StringBuilder DeviceFlashSectorMap { get; set; }

        public StringBuilder DeviceMemoryMap { get; set; }

        public StringBuilder DeviceSystemInfo { get; set; }

        public int LastDeviceHash { get; set; }

        private async Task LoadDeviceInfo()
        {
            // sanity check
            if (MainVM.SelectedDevice == null)
                return;

            // if same device nothing to do here, exit
            if (MainVM.SelectedDevice.Description.GetHashCode() == LastDeviceHash)
                return;

            // keep device description hash code to avoid get info twice
            LastDeviceHash = MainVM.SelectedDevice.Description.GetHashCode();

            // launch busy indicator
            MainVM.BusySrv.ShowBusy(Res.GetString("GettingDeviceInfoBusy"));

            try
            {
                // get device info
                var di = await MainVM.SelectedDevice.GetDeviceInfoAsync();
                var mm = await MainVM.SelectedDevice.DebugEngine.GetMemoryMapAsync();
                var fm = await MainVM.SelectedDevice.DebugEngine.GetFlashSectorMapAsync();
                var dm = await MainVM.SelectedDevice.DebugEngine.GetDeploymentMapAsync();

                // load properties for maps
                DeviceMemoryMap = new StringBuilder(mm?.ToStringForOutput() ?? "Empty");
                DeviceFlashSectorMap = new StringBuilder(fm?.ToStringForOutput() ?? "Empty");
                DeviceDeploymentMap = new StringBuilder(dm?.ToStringForOutput() ?? "Empty");
                // and system
                DeviceSystemInfo = new StringBuilder(di?.ToString() ?? "Empty");
            }
            catch
            {
                // reset prop to force a new get on next time we navigate into this page
                LastDeviceHash = 0;
            }

            // stop busy
            MainVM.BusySrv.HideBusy();
        }

        /// <summary>
        /// Copy all info from all pivots to clipboard
        /// </summary>
        public void CopyAllInfo()
        {
            StringBuilder st = new StringBuilder();

            // get all info from available pivots
            st.AppendLine(DeviceSystemInfo.ToString());
            st.AppendLine(""); // only to give it an extra line between infos
            st.AppendLine(Res.GetString("DC_DeviceMemoryMapTitle/Text"));
            st.AppendLine(DeviceMemoryMap.ToString());
            st.AppendLine(Res.GetString("DC_DeviceFlashSectorMapTitle/Text"));
            st.AppendLine(DeviceFlashSectorMap.ToString());
            st.AppendLine(Res.GetString("DC_DeviceDeploymentMapTitle/Text"));
            st.AppendLine(DeviceDeploymentMap.ToString());

            // prepare data package for clipboard
            DataPackage dp = new DataPackage();
            dp.SetText(st.ToString());
            // load it to clipboard
            Clipboard.SetContent(dp);
        }

        public int CurrentPivot { get; set; }

        /// <summary>
        /// Copy info from active pivot to clipboard
        /// </summary>
        public void CopyCurrentInfo()
        {
            // prepare data package for clipboard
            DataPackage dp = new DataPackage();
            switch (CurrentPivot)
            {
                case 0: // System
                    dp.SetText(DeviceSystemInfo.ToString());
                    break;
                case 1: // Memory
                    dp.SetText(Res.GetString("DC_DeviceMemoryMapTitle/Text") + Environment.NewLine + DeviceMemoryMap + Environment.NewLine + Res.GetString("DC_DeviceFlashSectorMapTitle/Text") + Environment.NewLine + DeviceFlashSectorMap + Res.GetString("DC_DeviceDeploymentMapTitle/Text") + Environment.NewLine + DeviceDeploymentMap);
                    break;
            }
            // load it to clipboard
            Clipboard.SetContent(dp);
        }
    }
}
