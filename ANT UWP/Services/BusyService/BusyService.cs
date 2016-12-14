using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Template10.Common;

namespace MFDeploy.Services.BusyService
{
    public class BusyService : IBusyService
    {
        public void ShowBusy(string busyText = null)
        {
            WindowWrapper.Current().Dispatcher.Dispatch(() =>
            {
                Views.Busy.SetBusy(true, busyText);
            });
        }


        public void HideBusy()
        {
            WindowWrapper.Current().Dispatcher.Dispatch(() =>
            {
                Views.Busy.SetBusy(false);
            });

        }
    }
}
