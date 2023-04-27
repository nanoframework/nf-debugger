//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using Polly;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Threading;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    public class PortSerial : PortMessageBase, IPort
    {
        private readonly PortSerialManager _portManager;
        public override event EventHandler<StringEventArgs> LogMessageAvailable;

        public SerialPort Device => (SerialPort)NanoDevice.DeviceBase;

        // valid baud rates
        public static readonly List<int> ValidBaudRates = new List<int>() { 921600, 460800, 115200 };

        public int BaudRate { get; internal set; }

        public NanoDevice<NanoSerialDevice> NanoDevice { get; }

        /// <summary>
        /// This DeviceInformation represents which device is connected or which device will be reconnected when
        /// the device is plugged in again (if IsEnabledAutoReconnect is true);.
        /// </summary>
        public string InstanceId
        {
            get
            {
                return NanoDevice.DeviceId;
            }
        }

        /// <summary>
        /// Creates an Serial debug client
        /// </summary>
        /// <param name="deviceInfo">Device information of the device to be opened</param>
        /// <param name="deviceSelector">The AQS used to find this device</param>
        public PortSerial(PortSerialManager portManager, NanoDevice<NanoSerialDevice> serialDevice)
        {
            _portManager = portManager ?? throw new ArgumentNullException(nameof(portManager));
            NanoDevice = serialDevice ?? throw new ArgumentNullException(nameof(serialDevice));

            // init default baud rate with 1st value
            BaudRate = ValidBaudRates[0];
        }

        #region SerialDevice methods

        /// <summary>
        /// This method opens the device using the WinRT Serial API. After the device is opened, save the device
        /// so that it can be used across scenarios.
        ///
        /// It is important that the FromIdAsync call is made on the UI thread because the consent prompt can only be displayed
        /// on the UI thread.
        /// 
        /// This method is used to reopen the device after the device reconnects to the computer and when the app resumes.
        /// </summary>
        /// <returns>True if the device was successfully opened, false if the device could not be opened for well known reasons.
        /// An exception may be thrown if the device could not be opened for extraordinary reasons.</returns>
        public ConnectPortResult OpenDevice()
        {
            bool successfullyOpenedDevice = false;

            /////////////////////////////////////////////////////////////
            // need to FORCE the parity setting to _NONE_ because        
            // the default on the current ST Link is different causing 
            // the communication to fail
            /////////////////////////////////////////////////////////////

            NanoDevice.DeviceBase = new SerialPort(InstanceId, BaudRate, Parity.None, 8);

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Device.DtrEnable = true;
                Device.RtsEnable = true;
            }

            // Device could have been blocked by user or the device has already been opened by another app.
            if (Device != null)
            {
                Device.Open();

                successfullyOpenedDevice = true;

                // set conservative timeouts
                Device.WriteTimeout = 5000;
                Device.ReadTimeout = 500;
                Device.ErrorReceived += Device_ErrorReceived;

                // better make sure the RX FIFO it's cleared
                Device.DiscardInBuffer();
            }
            else
            {
                successfullyOpenedDevice = false;
            }

            return successfullyOpenedDevice ? ConnectPortResult.Connected : ConnectPortResult.NotConnected;
        }

        public bool IsDeviceConnected
        {
            get
            {
                return (Device != null);
            }
        }

        /// <summary>
        /// Closes the device, stops the device watcher, stops listening for app events, and resets object state to before a device
        /// was ever connected.
        /// </summary>
        public void CloseDevice()
        {
            if (NanoDevice.DeviceBase != null)
            {
                try
                {
                    if (((SerialPort)NanoDevice.DeviceBase).IsOpen)
                    {
                        ((SerialPort)NanoDevice.DeviceBase).Close();
                    }
                    ((SerialPort)NanoDevice.DeviceBase).Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($">>>> CloseDevice ERROR from {InstanceId}: {ex.Message}");
                }
            }
        }

        private void Device_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            Debug.WriteLine($">>>> Serial ERROR from {InstanceId}: {e.EventType}");
        }

        #endregion

        public ConnectPortResult ConnectDevice()
        {
            ConnectPortResult openDeviceResult = ConnectPortResult.NotConnected;

            // try to determine if we already have this device opened.
            if (Device != null &&
                Device.IsOpen)
            {
                // better make sure the RX FIFO it's cleared
                Device.DiscardInBuffer();

                return ConnectPortResult.Connected;
            }

            try
            {
                openDeviceResult = Policy.Handle<IOException>()
                    .Or<UnauthorizedAccessException>()
                    .Or<Exception>()
                    .WaitAndRetry(10, retryCount => TimeSpan.FromMilliseconds((retryCount * retryCount) * 25),
                        onRetry: (exception, delay, retryCount, context) => LogRetry(exception, delay, retryCount, context))
                    .Execute(() => OpenDevice());

                if (openDeviceResult == ConnectPortResult.Connected)
                {
                    OnLogMessageAvailable(NanoDevicesEventSource.Log.OpenDevice(InstanceId));
                }
                else 
                {
                    // Most likely the device is opened by another app, but cannot be sure
                    OnLogMessageAvailable(NanoDevicesEventSource.Log.CriticalError($"Can't open Device: {InstanceId}"));
                }
            }
            catch (UnauthorizedAccessException uaEx)
            {
                // Most likely the device is opened by another app, but cannot be sure
                var logMsg = ($"Error in ConnectDevice: {uaEx.Message} Can't open {InstanceId}, possibly opened by another app");
                Debug.WriteLine(logMsg);
                OnLogMessageAvailable(NanoDevicesEventSource.Log.CriticalError(logMsg));
                openDeviceResult = ConnectPortResult.Unauthorized;
            }
            catch (Exception ex)
            {
                // Most likely the device is opened by another app, but cannot be sure
                var logMsg = ($"Error in ConnectDevice: {ex.Message} Unknown error opening {InstanceId}");
                Debug.WriteLine(logMsg);
                OnLogMessageAvailable(NanoDevicesEventSource.Log.CriticalError(logMsg));
                openDeviceResult = ConnectPortResult.ExceptionOccurred;
            }

            return openDeviceResult;
        }

        private void LogRetry(Exception response, TimeSpan delay, object retryCount, object context)
        {
            string logMsg = $"Can't open {InstanceId}: {response.Message} retryCount: {retryCount}, delay msec: {delay.TotalMilliseconds}";

            Debug.WriteLine(logMsg);
            OnLogMessageAvailable(NanoDevicesEventSource.Log.CriticalError(logMsg));
        }

        public void DisconnectDevice(bool force = false)
        {
            // disconnecting the current device

            OnLogMessageAvailable(NanoDevicesEventSource.Log.CloseDevice(InstanceId));

            // close device
            CloseDevice();

            if (force)
            {
                _portManager.DisposeDevice(InstanceId);
            }
        }
        private void OnLogMessageAvailable(string message)
        {
            LogMessageAvailable?.Invoke(this, new StringEventArgs(message));
        }

        #region Interface implementations

        public DateTime LastActivity { get; set; }

        public int AvailableBytes
        {
            get
            {
                if (Device.IsOpen)
                {
                    return Device.BytesToRead;
                }
                else
                {
                    // return -1 to signal that the port is not open
                    return -1;
                }
            }
        }

        public int SendBuffer(byte[] buffer)
        {
            // device must be connected
            if (IsDeviceConnected &&
                Device.IsOpen)
            {
                try
                {
                    // write buffer to device
                    Device.Write(buffer, 0, buffer.Length);

                    return buffer.Length;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SendRawBufferAsync-Serial-Exception occurred: {ex.Message}\r\n {ex.StackTrace}");

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
            // device must be connected
            if (IsDeviceConnected &&
                Device.IsOpen)
            {
                byte[] buffer = new byte[bytesToRead];

                try
                {
                    int readBytes = Device.Read(buffer, 0, bytesToRead);

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
                    Debug.WriteLine($"ReadBufferAsync-Serial-Exception occurred: {ex.Message}\r\n {ex.StackTrace}");

                    throw;
                }
            }
            else
            {
                throw new DeviceNotConnectedException();
            }

            // return empty byte array
            return new byte[0];
        }

        #endregion

    }
}
