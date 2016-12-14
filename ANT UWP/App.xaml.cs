using Windows.UI.Xaml;
using System.Threading.Tasks;
using MFDeploy.Services.SettingsServices;
using Windows.ApplicationModel.Activation;
using Template10.Controls;
using Template10.Common;
using System;
using System.Linq;
using Windows.UI.Xaml.Data;
using MFDeploy.Views.Config;
using Microsoft.NetMicroFramework.Tools.UsbDebug;
using System.Diagnostics;
using MFDeploy.Services.NetMicroFrameworkService;
using GalaSoft.MvvmLight.Ioc;
using MFDeploy.ViewModels;
using Microsoft.Practices.ServiceLocation;

namespace MFDeploy
{
    /// Documentation on APIs used in this page:
    /// https://github.com/Windows-XAML/Template10/wiki

    [Bindable]
    sealed partial class App : Template10.Common.BootStrapper
    {
        public App()
        {
            InitializeComponent();
            SplashFactory = (e) => new Views.Splash(e);

            #region App settings

            var _settings = SettingsService.Instance;
            RequestedTheme = _settings.AppTheme;
            CacheMaxDuration = _settings.CacheMaxDuration;
            //ShowShellBackButton = _settings.UseShellBackButton;
            ShowShellBackButton = false;
            #endregion
        }

        public override async Task OnInitializeAsync(IActivatedEventArgs args)
        {
            if (Window.Current.Content as ModalDialog == null)
            {
                // setup Page keys for navigation on mvvm
                var keys = PageKeys<Pages>();
                PagesHelper.SetupPages(keys);

                // create a new frame 
                var nav = NavigationServiceFactory(BackButton.Attach, ExistingContent.Include);

                // create modal root
                Window.Current.Content = new ModalDialog
                {
                    DisableBackButtonWhenModal = true,                    
                    Content = new Views.Shell(nav),
                    ModalContent = new Views.Busy(),
                };

                var usbClient = CreateUSBDebugClient();
                ServiceLocator.Current.GetInstance<MainViewModel>().UsbDebugService = usbClient;
            }
            await Task.CompletedTask;
        }
        private  INetMFUsbDebugClientService CreateUSBDebugClient()
        {
            // TODO: check app lifecycle
            var usbDebugClient = new UsbDebugClient(App.Current);
            return new NetMFUsbDebugClientService(usbDebugClient);
        }

        public override async Task OnStartAsync(StartKind startKind, IActivatedEventArgs args)
        {
            // long-running startup tasks go here
            await Task.Delay(1000);

            NavigationService.Navigate(Pages.MainPage);
            await Task.CompletedTask;
        }
    }
}

