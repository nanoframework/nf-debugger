// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using nanoFramework.Tools.Debugger.NFDevice;

namespace nanoFramework.Tools.Debugger.PortTcpIp
{
    public class PortTcpIp : PortMessageBase, IPort
    {
        private readonly PortTcpIpManager _portManager;
        private GlobalExclusiveDeviceAccess _exclusiveAccess;

        private volatile NetworkStream _stream;

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
            var stream = _stream;

            if (stream is not null && NanoNetworkDevice?.Connected == true)
            {
                try
                {
                    stream.Write(buffer, 0, buffer.Length);

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
            var stream = _stream;

            if (stream is not null && NanoNetworkDevice?.Connected == true)
            {
                if (bytesToRead <= 0)
                {
                    // a zero length read returns 0, which isn't a disconnection
                    return Array.Empty<byte>();
                }

                byte[] buffer = new byte[bytesToRead];

                try
                {
                    int readBytes = stream.Read(buffer, 0, bytesToRead);

                    if (readBytes == 0)
                    {
                        // the other side has closed the connection
                        throw new DeviceNotConnectedException("Connection closed by the device.");
                    }

                    if (readBytes != bytesToRead)
                    {
                        Array.Resize(ref buffer, readBytes);
                    }

                    return buffer;
                }
                catch (IOException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.TimedOut })
                {
                    // this is expected to happen when the timeout occurs, no need to do anything with it
                }
                catch (TimeoutException)
                {
                    // this is expected to happen when the timeout occurs, no need to do anything with it
                }
                catch (DeviceNotConnectedException)
                {
                    CloseDeviceIfCurrent(stream);

                    throw;
                }
                catch (Exception ex) when (ex is IOException || ex is ObjectDisposedException || ex is SocketException)
                {
                    Debug.WriteLine($"Read-NetworkStream-Exception  occurred: {ex.Message}\r\n {ex.StackTrace}");

                    // the connection is gone
                    CloseDeviceIfCurrent(stream);

                    throw new DeviceNotConnectedException(ex.Message, ex);
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
            _exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(InstanceId);
            if (_exclusiveAccess is null)
            {
                return ConnectPortResult.NoExclusiveAccess;
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
            finally
            {
                if (NanoNetworkDevice?.Connected != true)
                {
                    _exclusiveAccess.Dispose();
                    _exclusiveAccess = null;
                }
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

        /// <summary>
        /// A reader that was left behind by a previous connection must not close the one that replaced it.
        /// </summary>
        private void CloseDeviceIfCurrent(System.IO.Stream stream)
        {
            if (ReferenceEquals(_stream, stream))
            {
                CloseDevice();
            }
        }

        private void CloseDevice()
        {
            try
            {
                _stream = null;

                // closing clears the client and stream, the device can connect again later
                NanoNetworkDevice.Close();
            }
            finally
            {
                _exclusiveAccess?.Dispose();
                _exclusiveAccess = null;
            }
        }

        private void OnLogMessageAvailable(string message)
        {
            LogMessageAvailable?.Invoke(this, new StringEventArgs(message));
        }
    }
}
