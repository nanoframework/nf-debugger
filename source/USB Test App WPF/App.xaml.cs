﻿//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using Microsoft.Practices.ServiceLocation;
using nanoFramework.ANT.Services.NanoFrameworkService;
using nanoFramework.Tools.Debugger;
using Serial_Test_App_WPF.ViewModel;
using System;

namespace Serial_Test_App_WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public ViewModelLocator vml;

        public App()
        {
            this.Activated += App_Activated;
            this.Deactivated += App_Deactivated;

            vml = new ViewModelLocator();

            var serialClient = CreateSerialDebugClient();
            ServiceLocator.Current.GetInstance<MainViewModel>().SerialDebugService = serialClient;
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
            //virtualApp = Windows.UI.Xaml.Application.Current;

            // TODO: check app lifecycle
            var serialDebugClient = PortBase.CreateInstanceForSerial("");

            return new NFSerialDebugClientService(serialDebugClient);
        }
    }

}
