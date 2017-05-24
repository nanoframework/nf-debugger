//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using GalaSoft.MvvmLight.Messaging;
using NanoFramework.ANT.Services.BusyService;
using NanoFramework.ANT.Services.Dialog;
using NanoFramework.ANT.Utilities;
using NanoFramework.ANT.Views.Config;
using Microsoft.Practices.ServiceLocation;
using Template10.Services.NavigationService;
using Windows.UI.Xaml.Navigation;

namespace NanoFramework.ANT.ViewModels
{
    public class ConfigNetworkViewModel : MyViewModelBase
    {
        //private instance of Main to get general stuff
        private MainViewModel MainVM { get { return ServiceLocator.Current.GetInstance<MainViewModel>(); } }


        public ConfigNetworkViewModel(IMyDialogService dlg, IBusyService busy)
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

            MessengerInstance.Register<NotificationMessage>(this, MainViewModel.SELECTED_NULL_TOKEN, (message) => SelectedIsNullHandler());

            await Task.CompletedTask;

            MainVM.PageHeader = Res.GetString("CN_PageHeader");
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

        #endregion
        private void SelectedIsNullHandler()
        {
            this.NavigationService.Navigate(Pages.MainPage);
        }

        private string _staticIPAdrress = "0.0.0.0";

        public string StaticIPAdrress
        {
            get { return _staticIPAdrress; }
            set { _staticIPAdrress = value; }
        }

        private string _subnetMask = "0.0.0.0";

        public string SubnetMask
        {
            get { return _subnetMask; }
            set { _subnetMask = value; }
        }

        private string _defaultGateway = "0.0.0.0";

        public string DefaultGateway
        {
            get { return _defaultGateway; }
            set { _defaultGateway = value; }
        }

        private string _macAdrress = "00:00:00:00:00:00";

        public string MACAdrress
        {
            get { return _macAdrress; }
            set { _macAdrress = value; }
        }

        private string _dnsPrimaryAdrress = "0.0.0.0";

        public string DNSPrimaryAdrress
        {
            get { return _dnsPrimaryAdrress; }
            set { _dnsPrimaryAdrress = value; }
        }

        private string _dnsSecondaryAdrress = "0.0.0.0";

        public string DNSSecondaryAdrress
        {
            get { return _dnsSecondaryAdrress; }
            set { _dnsSecondaryAdrress = value; }
        }

        private Nullable<Boolean> _dhcpEnable = false;

        public Nullable<Boolean> DHCPEnable
        {
            get { return _dhcpEnable; }
            set { _dhcpEnable = value; }
        }

        private Boolean _updateButtonEnabled;

        public Boolean UpdateButtonEnabled
        {
            get { return _updateButtonEnabled; }
            set { _updateButtonEnabled = value; }
        }


        #region Wireless

        private int _authentication;

        public int Authentication
        {
            get { return _authentication; }
            set { _authentication = value; }
        }

        private int _encryption;

        public int Encryption
        {
            get { return _encryption; }
            set { _encryption = value; }
        }

        private RadioTypes _radio = RadioTypes.a;

        public RadioTypes Radio
        {
            get { return _radio; }
            set { _radio = value; }
        }


        private Nullable<Boolean> _encryptConfigData = false;

        public Nullable<Boolean> EncryptConfigData
        {
            get { return _encryptConfigData; }
            set { _encryptConfigData = value; }
        }

        private string _passPhrase;

        public string PassPhrase
        {
            get { return _passPhrase; }
            set { _passPhrase = value; }
        }

        private string _networkKey = "";

        public string NetworkKey
        {
            get { return _networkKey; }
            set { _networkKey = value; }
        }

        private string _reKeyInternal;

        public string ReKeyInternal
        {
            get { return _reKeyInternal; }
            set { _reKeyInternal = value; }
        }

        private string _ssid;

        public string SSID
        {
            get { return _ssid; }
            set { _ssid = value; }
        }

        #endregion

        public void UpdateConfiguration()
        {
            bool success = true;

            // show busy
            BusySrv.ShowBusy();

            // TBD
            // the code to update config goes here...

            // end busy
            BusySrv.HideBusy();

            // show result to user
            if (success)
                DialogSrv.ShowMessageAsync(Res.GetString("CU_ConfigurationUpdated"));
            else
                DialogSrv.ShowMessageAsync(Res.GetString("CU_FailToUpdateConfiguration"));
        }
    }

    public enum RadioTypes : int
    {
        a = 1,
        b = 2,
        g = 4,
        n = 8,
    }

}
