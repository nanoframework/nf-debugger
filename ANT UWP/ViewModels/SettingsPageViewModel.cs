//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NanoFramework.ANT.Services.BusyService;
using NanoFramework.ANT.Services.Dialog;
using NanoFramework.ANT.Services.SettingsServices;
using NanoFramework.ANT.Utilities;
using Microsoft.Practices.ServiceLocation;
using Template10.Services.NavigationService;
using Windows.UI.Xaml.Navigation;
using Windows.Storage;
using NanoFramework.ANT.Services.StorageService;

namespace NanoFramework.ANT.ViewModels
{
    public class SettingsPageViewModel : MyViewModelBase
    {
        //private instance of Main to get general stuff
        private MainViewModel MainVM { get { return ServiceLocator.Current.GetInstance<MainViewModel>(); } }

        public SettingsPageViewModel(IMyDialogService dlg, IBusyService busy, IAppSettingsService _appSettings, IStorageInterfaceService _storageInterfaceService)
        {
            SettingsPartViewModel  = new SettingsPartViewModel(dlg, busy, _appSettings, _storageInterfaceService);
            AboutPartViewModel = new AboutPartViewModel(dlg, busy);
        }
        public SettingsPartViewModel SettingsPartViewModel { get; }
        public AboutPartViewModel AboutPartViewModel { get; }

        #region Navigation
        public override async Task OnNavigatedToAsync(object parameter, NavigationMode mode, IDictionary<string, object> suspensionState)
        {

            if (suspensionState.Any())
            {
                //Value = suspensionState[nameof(Value)]?.ToString();
            }
            await Task.CompletedTask;

            MainVM.PageHeader = Res.GetString("ST_PageHeader");

            await SettingsPartViewModel.LoadDeployFolder();
        }

        public override async Task OnNavigatedFromAsync(IDictionary<string, object> suspensionState, bool suspending)
        {
            if (suspending)
            {
                //suspensionState[nameof(Value)] = Value;
            }
            await Task.CompletedTask;
        }

        public override async Task OnNavigatingFromAsync(NavigatingEventArgs args)
        {
            args.Cancel = false;
            await Task.CompletedTask;
        }

        #endregion
    }

    public class SettingsPartViewModel : MyViewModelBase
    {
        IAppSettingsService _settings;

        public SettingsPartViewModel(IMyDialogService dlg, IBusyService busy, IAppSettingsService _appSettings, IStorageInterfaceService _storageInterfaceService)
        {
            this.DialogSrv = dlg;
            this.BusySrv = busy;
            this._settings = _appSettings;
            this.StorageInterface = _storageInterfaceService;
        }

        public bool AddTimestampToOutputButton
        {
            get { return _settings.AddTimestampToOutput; }
            set { _settings.AddTimestampToOutput = value; }
        }
        public string DeployFolderPath { get; set; }

        public async Task LoadDeployFolder()
        {
            StorageFolder folder = await StorageInterface.GetDeployFolder();

            if (folder != null)
            {
                DeployFolderPath = folder.Path;
            }
            else
                DeployFolderPath = Res.GetString("ST_CurrentFolderPath");
        }

        public async Task PickDeployFolder()
        {
            string folderPath = await StorageInterface.PickDeployFolder();
            if (folderPath != String.Empty)
            {
                DeployFolderPath = folderPath;
            }
        }
    }

    public class AboutPartViewModel : MyViewModelBase
    {
        public AboutPartViewModel(IMyDialogService dlg, IBusyService busy)
        {
            this.DialogSrv = dlg;
            this.BusySrv = busy;
        }

        public Uri Logo => Windows.ApplicationModel.Package.Current.Logo;

        public string DisplayName => Windows.ApplicationModel.Package.Current.DisplayName;

        public string Publisher => Windows.ApplicationModel.Package.Current.PublisherDisplayName;

        public string Concept
        {
            get
            {
                return $"{Res.GetString("ST_Concept")} Eclo Solutions";
            }
        }

        public string Version
        {
            get
            {
                var v = Windows.ApplicationModel.Package.Current.Id.Version;
                return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            }
        }

        public Uri RateMe => new Uri("http://aka.ms/template10");
    }

    
}

