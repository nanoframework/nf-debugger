//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Template10.Common;

namespace NanoFramework.ANT.Services.BusyService
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

        public void ChangeBusyText(string newBusyText)
        {
            WindowWrapper.Current().Dispatcher.Dispatch(() =>
            {
                Views.Busy.ChangeBusyText(newBusyText);
            });
        }
    }
}
