using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MFDeploy.Views.Config
{
    public abstract class PagesHelper
    {
        public static void SetupPages(Dictionary<Pages, Type> keys)
        {
            keys.Add(Pages.MainPage, typeof(MainPage));
            keys.Add(Pages.SettingsPage, typeof(SettingsPage));
            keys.Add(Pages.DeployPage, typeof(DeployPage));
            keys.Add(Pages.ConfigUSBPage, typeof(ConfigUSBPage));
            keys.Add(Pages.ConfigNetworkPage, typeof(ConfigNetworkPage));
            keys.Add(Pages.DeviceCapabilitiesPage, typeof(DeviceCapabilitiesPage)); 
        }
    }
}
