using System;
using System.Collections.Generic;
using System.Linq;

namespace MFDeploy.Models
{
    public enum ConnectionState
    {
        None = 0,
        ConnectAvailable,
        Connecting,
        DisconnectAvailable,
        Disconnecting
    }
}
