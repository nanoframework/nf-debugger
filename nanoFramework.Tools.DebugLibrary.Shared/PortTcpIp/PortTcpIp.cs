// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.Net.Sockets;

namespace nanoFramework.Tools.Debugger.PortTcpIp
{
    public class PortTcpIp : PortMessageBase, IPort
    {
        private readonly PortTcpIpManager _portManager;

        private NetworkStream _stream;

        private NanoDevice<NanoNetworkDevice> NanoDevice { get; }

        private NanoNetworkDevice NanoNetworkDevice => NanoDevice.Device;

        public string InstanceId => NanoDevice.DeviceId;

        public override event EventHandler<StringEventArgs> LogMessageAvailable;

        public PortTcpIp(PortTcpIpManager portManager, NanoDevice<NanoNetworkDevice> networkDevice, NetworkDeviceInformation deviceInformation)
        {
            _portManager = portManager ?? throw new ArgumentNullException(nameof(portManager));
            NanoDevice = networkDevice ?? throw new ArgumentNullException(nameof(networkDevice));
            NanoDevice.Device.NetworkDeviceInformation = deviceInformation;
        }

        public int AvailableBytes => NanoNetworkDevice.AvailableBytes;

        public int SendBuffer(byte[] buffer)
        {
            if (NanoNetworkDevice?.Connected == true)
            {
                try
                {
                    _stream.Write(buffer, 0, buffer.Length);

                    return buffer.Length;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Write-NetworkStream-Exception occurred: {ex.Message}\r\n {ex.StackTrace}");
                    throw;
                }
            }
            else
            {
                throw new DeviceNotConnectedException();
            }
        }

        public byte[] ReadBuffer(int bytesToRead)
        {
            if (NanoNetworkDevice?.Connected == true)
            {
                byte[] buffer = new byte[bytesToRead];

                try
                {
                    int readBytes = _stream.Read(buffer, 0, bytesToRead);

                    if (readBytes != bytesToRead)
                    {
                        Array.Resize(ref buffer, readBytes);
                    }

                    return buffer;
                }
                catch (TimeoutException)
                {
                    // this is expected to happen when the timeout occurs, no need to do anything with it
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Read-NetworkStream-Exception  occurred: {ex.Message}\r\n {ex.StackTrace}");

                    throw;
                }
            }
            else
            {
                throw new DeviceNotConnectedException();
            }

            return Array.Empty<byte>();
        }

        public ConnectPortResult ConnectDevice()
        {
            if (NanoNetworkDevice?.Connected == true)
            {
                return ConnectPortResult.Connected;
            }

            try
            {
                _stream = NanoNetworkDevice?.Connect();
            }
            catch (UnauthorizedAccessException)
            {
                return ConnectPortResult.Unauthorized;
            }
#if DEBUG
            catch (Exception ex)
#else
            catch
#endif
            {
                return ConnectPortResult.ExceptionOccurred;
            }

            return NanoNetworkDevice?.Connected == true ? ConnectPortResult.Connected : ConnectPortResult.NotConnected;
        }

        public void DisconnectDevice(bool force = false)
        {
            OnLogMessageAvailable(NanoDevicesEventSource.Log.CloseDevice(InstanceId));

            CloseDevice();

            if (force)
            {
                _portManager.DisposeDevice(InstanceId);
            }
        }

        private void CloseDevice()
        {
            NanoNetworkDevice.Close();
            NanoNetworkDevice.Dispose();
        }

        private void OnLogMessageAvailable(string message)
        {
            LogMessageAvailable?.Invoke(this, new StringEventArgs(message));
        }
    }
}
