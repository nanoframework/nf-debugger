//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Windows;
using nanoFramework.ANT.Services.NanoFrameworkService;
using nanoFramework.Tools.Debugger;
using Serial_Test_App_WPF.ViewModel;

namespace Serial_Test_App_WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        private readonly MainViewModel _mainViewModel = new MainViewModel();

        public App()
        {
            Activated += App_Activated;
            Deactivated += App_Deactivated;
            Exit += App_Exit;

            _mainViewModel.SerialDebugService = CreateSerialDebugClient();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            MainWindow = new MainWindow(_mainViewModel);
            MainWindow.Show();
        }

        private void App_Exit(object sender, System.Windows.ExitEventArgs e)
        {
            _mainViewModel.SerialDebugService.SerialDebugClient.StopDeviceWatchers();
        }

        private void App_Deactivated(object sender, EventArgs e)
        {
            //throw new NotImplementedException();
        }

        private void App_Activated(object sender, EventArgs e)
        {
            //throw new NotImplementedException();
        }

        private INFSerialDebugClientService CreateSerialDebugClient()
        {
            // add here any COM ports to exclude from the search
            var devicesToExclude = new List<string>() { "COM3", "COM4", "COM14" };

            var composite = PortBase.CreateInstanceForComposite(new[]
            {
                PortBase.CreateInstanceForSerial(false, devicesToExclude), 
                //PortBase.CreateInstanceForNetwork() 
            }, true);

            return new NFSerialDebugClientService(composite);
        }
    }
}
