//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;

namespace NanoFramework.ANT.Services.BusyService
{
    public interface IBusyService
    {
        void ShowBusy(string busyText = null);
        void HideBusy();
        void ChangeBusyText(string newBusyText);
    }
}
