using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.NetMicroFramework.Tools.UsbDebug;

namespace MFDeploy.Services.NetMicroFrameworkService
{
    public class NetMFUsbDebugClientService : INetMFUsbDebugClientService
    {
        public UsbDebugClient UsbDebugClient { get; private set; }

        public NetMFUsbDebugClientService(UsbDebugClient client)
        {
            this.UsbDebugClient = client;
        }

    }
}
