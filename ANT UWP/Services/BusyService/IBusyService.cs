using System;
using System.Collections.Generic;
using System.Linq;

namespace MFDeploy.Services.BusyService
{
    public interface IBusyService
    {
        void ShowBusy(string busyText = null);
        void HideBusy();
    }
}
