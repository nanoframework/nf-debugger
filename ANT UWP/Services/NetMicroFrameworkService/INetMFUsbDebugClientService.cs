using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.NetMicroFramework.Tools.UsbDebug;

namespace MFDeploy.Services.NetMicroFrameworkService
{
    public interface INetMFUsbDebugClientService : INetMFDebugClientBaseService
    {
        UsbDebugClient UsbDebugClient { get; }

    }
}
