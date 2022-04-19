//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.WireProtocol;
using PropertyChanged;
using System;
using nanoFramework.Tools.Debugger.PortTcpIp;

namespace nanoFramework.Tools.Debugger
{
    [AddINotifyPropertyChangedInterface]
    public class NanoDevice<T> : NanoDeviceBase, IDisposable, INanoDevice where T : new()
    {
        public T Device { get; set; }

        public string DeviceId { get; set; }

        public NanoDevice()
        {
            Device = new T();

            if (Device is NanoSerialDevice)
            {
                Transport = TransportType.Serial;
            }
        }

        #region Disposable implementation

        public bool disposed { get; private set; }

        ~NanoDevice()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    try
                    {
                        // release managed components
                        Disconnect();
                    }
                    catch
                    {
                        // required to catch exceptions from Engine dispose calls
                    }

                    disposed = true;
                }
            }
        }

        /// <summary>
        /// Standard Dispose method for releasing resources such as the connection to the device.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        #endregion

        /// <summary>
        /// Connect to nanoFramework device
        /// </summary>
        /// <returns><see cref="ConnectPortResult"/> result after attempting to connect to the device.</returns>
        public ConnectPortResult Connect()
        {
            if (Device is NanoSerialDevice || Device is NanoNetworkDevice)
            {
                return ConnectionPort.ConnectDevice();
            }
            
            return ConnectPortResult.NotConnected;
        }

        /// <summary>
        /// Disconnect nanoFramework device
        /// </summary>
        public override void Disconnect(bool force = false)
        {
            ConnectionPort.DisconnectDevice(force);
        }
    }
}
