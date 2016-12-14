using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MFDeploy.Services.BusyService;
using MFDeploy.Services.Dialog;
using MFDeploy.Utilities;
using Microsoft.Practices.ServiceLocation;
using Template10.Mvvm;
using Template10.Services.NavigationService;
using Template10.Services.SettingsService;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Navigation;

namespace MFDeploy.ViewModels
{
    public class SettingsPageViewModel : MyViewModelBase
    {
        //private instance of Main to get general stuff
        private MainViewModel MainVM { get { return ServiceLocator.Current.GetInstance<MainViewModel>(); } }

        public SettingsPageViewModel(IMyDialogService dlg, IBusyService busy)
        {
            SettingsPartViewModel  = new SettingsPartViewModel(dlg, busy);
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
        Services.SettingsServices.SettingsService _settings;

        public SettingsPartViewModel(IMyDialogService dlg, IBusyService busy)
        {
            if (Windows.ApplicationModel.DesignMode.DesignModeEnabled)
            {
                // designtime
            }
            else
            {
                _settings = Services.SettingsServices.SettingsService.Instance;
            }
            this.DialogSrv = dlg;
            this.BusySrv = busy;
        }

        public bool UseShellBackButton
        {
            get { return _settings.UseShellBackButton; }
            set { _settings.UseShellBackButton = value; base.RaisePropertyChanged(); }
        }

        public bool UseLightThemeButton
        {
            get { return _settings.AppTheme.Equals(ApplicationTheme.Light); }
            set { _settings.AppTheme = value ? ApplicationTheme.Light : ApplicationTheme.Dark; base.RaisePropertyChanged(); }
        }

        private string _BusyText = "Please wait...";
        public string BusyText
        {
            get { return _BusyText; }
            set
            {
                Set(ref _BusyText, value);
                _ShowBusyCommand.RaiseCanExecuteChanged();
            }
        }

        DelegateCommand _ShowBusyCommand;
        public DelegateCommand ShowBusyCommand
            => _ShowBusyCommand ?? (_ShowBusyCommand = new DelegateCommand(async () =>
            {
                BusySrv.ShowBusy(_BusyText);
                await Task.Delay(5000);
                BusySrv.HideBusy();
            }, () => !string.IsNullOrEmpty(BusyText)));
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

