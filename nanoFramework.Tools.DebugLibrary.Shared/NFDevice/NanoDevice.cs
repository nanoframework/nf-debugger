//
// Copyright (c) .NET Foundation and Contributors
// Portions Copyright (c) Microsoft Corporation.  All rights reserved.
// See LICENSE file in the project root for full license information.
//

using System;
using nanoFramework.Tools.Debugger.PortTcpIp;
using nanoFramework.Tools.Debugger.WireProtocol;

namespace nanoFramework.Tools.Debugger
{
    public partial class NanoDevice<T> : NanoDeviceBase, IDisposable, INanoDevice where T : new()
    {
        private bool _disposed;

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

        ~NanoDevice()
        {
            Dispose(false);
        }

        private void Dispose(bool disposing)
        {
            if (!_disposed)
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

                    _disposed = true;
                }
            }
        }

        /// <summary>
        /// Standard Dispose method for releasing resources such as the connection to the device.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
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
