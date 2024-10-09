// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using nanoFramework.Tools.Debugger.NFDevice;
using Polly;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    /// <summary>
    /// A class representing a serial port used for communication with a device.
    /// </summary>
    public class PortSerial : PortMessageBase, IPort
    {
        private readonly PortSerialManager _portManager;
        private GlobalExclusiveDeviceAccess _exclusiveAccess;

        /// <summary>
        /// Event that is raised when a log message is available.
        /// </summary>
        public override event EventHandler<StringEventArgs> LogMessageAvailable;

        /// <summary>
        /// Gets the underlying SerialPort device.
        /// </summary>
        public SerialPort Device => (SerialPort)NanoDevice.DeviceBase;

        // valid baud rates
        /// <summary>
        /// A list of valid baud rates for the serial port.
        /// </summary>
        public static readonly List<int> ValidBaudRates = new List<int>() { 921600, 460800, 115200 };

        /// <summary>
        /// Gets or sets the baud rate for the serial port.
        /// </summary>
        public int BaudRate { get; internal set; }

        /// <summary>
        /// Gets or sets the baud rate for the serial port.
        /// </summary>
        public NanoDevice<NanoSerialDevice> NanoDevice { get; }

        /// <summary>
        /// Gets the Instance ID of the device.
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
        /// Initializes a new instance of the PortSerial class.
        /// </summary>
        /// <param name="portManager">The manager for the serial port.</param>
        /// <param name="serialDevice">The NanoDevice associated with the serial port.</param>
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
            bool exclusiveAccessCreated = false;
            if (_exclusiveAccess is null)
            {
                _exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(InstanceId);
                if (_exclusiveAccess is null)
                {
                    return ConnectPortResult.NoExclusiveAccess;
                }
                exclusiveAccessCreated = true;
            }

            bool successfullyOpenedDevice = false;
            try
            {
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
            }
            finally
            {
                if (exclusiveAccessCreated && !successfullyOpenedDevice)
                {
                    _exclusiveAccess.Dispose();
                    _exclusiveAccess = null;
                }
            }

            return successfullyOpenedDevice ? ConnectPortResult.Connected : ConnectPortResult.NotConnected;
        }

        /// <summary>
        /// Gets a value indicating whether the device is connected.
        /// </summary>
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
            if (_exclusiveAccess is not null)
            {
                _exclusiveAccess.Dispose();
                _exclusiveAccess = null;
            }
        }

        private void Device_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            Debug.WriteLine($">>>> Serial ERROR from {InstanceId}: {e.EventType}");
        }

        #endregion

        /// <summary>
        /// Connects to a serial device.
        /// </summary>
        /// <returns>The result of the connection attempt</returns>
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

            bool exclusiveAccessCreated = false;
            if (_exclusiveAccess is null)
            {
                _exclusiveAccess = GlobalExclusiveDeviceAccess.TryGet(InstanceId);
                if (_exclusiveAccess is null)
                {
                    return ConnectPortResult.NoExclusiveAccess;
                }
                exclusiveAccessCreated = true;
            }
            try
            {
                openDeviceResult = Policy.Handle<IOException>()
                    .Or<UnauthorizedAccessException>()
                    .Or<Exception>()
                    .WaitAndRetry(10, retryCount => TimeSpan.FromMilliseconds(retryCount * 75),
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
            finally
            {
                if (exclusiveAccessCreated && openDeviceResult != ConnectPortResult.Connected)
                {
                    _exclusiveAccess.Dispose();
                    _exclusiveAccess = null;
                }
            }

            return openDeviceResult;
        }

        private void LogRetry(Exception response, TimeSpan delay, object retryCount, object context)
        {
            string logMsg = $"Can't open {InstanceId}: {response.Message} retryCount: {retryCount}, delay msec: {delay.TotalMilliseconds}";

            Debug.WriteLine(logMsg);
            OnLogMessageAvailable(NanoDevicesEventSource.Log.CriticalError(logMsg));
        }

        /// <summary>
        /// Disconnects the current device.
        /// </summary>
        /// <param name="force">Flag indicating whether the device should be forcibly disposed.</param>
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

        /// <summary>
        /// Gets or sets the date and time when the device was last active.
        /// </summary>
        public DateTime LastActivity { get; set; }

        /// <summary>
        /// Gets the number of bytes available in the receive buffer of the open serial port.
        /// If the port is not open, returns -1.
        /// </summary>
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

        /// <summary>
        /// Sends the specified byte array to the connected device.
        /// </summary>
        /// <param name="buffer">The byte array to send.</param>
        /// <returns>The number of bytes sent.</returns>
        /// <exception cref="DeviceNotConnectedException">Thrown when the device is not connected.</exception>
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

        /// <summary>
        /// Reads the specified number of bytes from the connected device. The device must be connected and open.
        /// </summary>
        /// <param name="bytesToRead">The number of bytes to read from the device.</param>
        /// <returns>A byte array containing the bytes read from the device. If an error occurs or the device is not connected or open, an empty byte array is returned.</returns>
        /// <exception cref="DeviceNotConnectedException">Thrown when the device is not connected or open.</exception>
        public byte[] ReadBuffer(int bytesToRead)
        {
            // device must be connected
            if (IsDeviceConnected &&
                Device.IsOpen)
            {
                byte[] buffer = new byte[bytesToRead];

                try
                {
                    // this loop shall fix issues with M1 and some drivers showing up bytes to fast
                    List<byte> received = new List<byte>();
                    int readBytes = 0;

                    while (Device.BytesToRead > 0 && readBytes < bytesToRead)
                    {
                        // compute how many bytes shall be read from the device
                        // so that we don't read more than what was requested
                        int missingBytes = Math.Min(Device.BytesToRead, bytesToRead - readBytes);

                        byte[] toRead = new byte[missingBytes];

                        // read them
                        readBytes += Device.Read(toRead, 0, toRead.Length);

                        // add to buffer
                        received.AddRange(toRead);
                    }

                    // resize read buffer, if needed
                    if (readBytes != bytesToRead)
                    {
                        Array.Resize(ref buffer, readBytes);
                    }

                    // copy over to read buffer
                    received.CopyTo(buffer);

                    // done here
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
