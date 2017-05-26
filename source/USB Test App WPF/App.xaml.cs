using Microsoft.Practices.ServiceLocation;
using nanoFramework.ANT.Services.NanoFrameworkService;
using nanoFramework.Tools.Debugger;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using USB_Test_App_WPF.ViewModel;

namespace USB_Test_App_WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        ViewModelLocator vml;

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
            // TODO: check app lifecycle
            var serialDebugClient = PortBase.CreateInstanceForSerial("", App.Current);

            return new NFSerialDebugClientService(serialDebugClient);
        }
    }

}
