// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using nanoFramework.Tools.Debugger.NFDevice;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    /// <summary>
    /// A class representing a serial port used for communication with a device.
    /// </summary>
    public class PortSerial : PortMessageBase, IPort
    {
        private readonly PortSerialManager _portManager;
        private GlobalExclusiveDeviceAccess _exclusiveAccess;

        // the port has been opened at least once: from then on, a missing port means it's gone (not that it isn't ready yet)
        private volatile bool _hasBeenOpened;

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
            if (!TryAcquireExclusiveAccess(null, out GlobalExclusiveDeviceAccess acquiredAccess))
            {
                return ConnectPortResult.NoExclusiveAccess;
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
                    _hasBeenOpened = true;

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
                if (!successfullyOpenedDevice)
                {
                    ReleaseExclusiveAccess(acquiredAccess);
                }
            }

            return successfullyOpenedDevice ? ConnectPortResult.Connected : ConnectPortResult.NotConnected;
        }

        /// <summary>
        /// Gets the exclusive access to the device, unless it's already held for this port.
        /// </summary>
        /// <param name="cancellationToken">Aborts waiting for the exclusive access.</param>
        /// <param name="acquiredAccess">The exclusive access acquired by this call, which the caller has to release
        /// with <see cref="ReleaseExclusiveAccess"/> if the device isn't opened. <see langword="null"/> if it was already held.</param>
        /// <returns><see langword="false"/> if the exclusive access couldn't be acquired.</returns>
        private bool TryAcquireExclusiveAccess(
            CancellationToken? cancellationToken,
            out GlobalExclusiveDeviceAccess acquiredAccess)
        {
            acquiredAccess = null;

            if (Volatile.Read(ref _exclusiveAccess) is not null)
            {
                return true;
            }

            var access = GlobalExclusiveDeviceAccess.TryGet(InstanceId, cancellationToken: cancellationToken);
            if (access is null)
            {
                return false;
            }

            if (Interlocked.CompareExchange(ref _exclusiveAccess, access, null) is not null)
            {
                // another thread has acquired it for this port in the meantime: keep that one
                access.Dispose();
            }
            else
            {
                acquiredAccess = access;
            }

            return true;
        }

        /// <summary>
        /// Releases the exclusive access acquired by <see cref="TryAcquireExclusiveAccess"/>, if this port still holds it.
        /// </summary>
        /// <remarks>
        /// <see cref="CloseDevice"/> can run on another thread while the device is being opened (e.g. the device has been unplugged)
        /// and release the exclusive access first: it must be released only once.
        /// </remarks>
        private void ReleaseExclusiveAccess(GlobalExclusiveDeviceAccess acquiredAccess)
        {
            if (acquiredAccess is not null
                && Interlocked.CompareExchange(ref _exclusiveAccess, null, acquiredAccess) == acquiredAccess)
            {
                acquiredAccess.Dispose();
            }
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
                    var serialPort = (SerialPort)NanoDevice.DeviceBase;

                    if (serialPort.IsOpen)
                    {
                        PrepareToClose(serialPort);

                        serialPort.Close();
                    }

                    serialPort.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($">>>> CloseDevice ERROR from {InstanceId}: {ex.Message}");
                }
            }
            // taken atomically: a connection attempt in progress on another thread may be releasing it too
            Interlocked.Exchange(ref _exclusiveAccess, null)?.Dispose();
        }

        /// <summary>
        /// Closing the port can block for ~30 s on some devices (seen with the native USB port of an ESP32-S3 right
        /// after it booted). SerialPort.Close() stops its event loop, then drops DTR and flushes the output.
        /// Doing these steps explicitly beforehand, while the port is still fully operational, avoids that
        /// (tested on hardware: purging alone isn't enough, dropping DTR and flushing beforehand are needed).
        /// </summary>
        private static void PrepareToClose(SerialPort serialPort)
        {
            try
            {
                var drainTimer = Stopwatch.StartNew();

                // give pending output a short time to go out...
                while (serialPort.BytesToWrite > 0
                       && drainTimer.ElapsedMilliseconds < 250)
                {
                    Thread.Sleep(10);
                }

                // ... then drop anything the driver still holds (it may not count what it already handed to USB)
                serialPort.DiscardOutBuffer();
                serialPort.DiscardInBuffer();

                serialPort.DtrEnable = false;
                serialPort.BaseStream.Flush();
            }
            catch
            {
                // the port may already be gone, closing it will sort that out
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
        public ConnectPortResult ConnectDevice() => ConnectDevice(CancellationToken.None);

        /// <summary>
        /// Connects to a serial device.
        /// </summary>
        /// <param name="cancellationToken">Aborts the connection attempt (including the retries to open the port).</param>
        /// <returns>The result of the connection attempt</returns>
        public ConnectPortResult ConnectDevice(CancellationToken cancellationToken)
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

            if (!TryAcquireExclusiveAccess(
                    cancellationToken.CanBeCanceled ? cancellationToken : null,
                    out GlobalExclusiveDeviceAccess acquiredAccess))
            {
                return ConnectPortResult.NoExclusiveAccess;
            }

            bool portGone = false;

            try
            {
                // retry on any error, unless the port is gone (e.g. device unplugged): it won't come back within the retries
                openDeviceResult = RetryHelper.Execute(
                    _ => OpenDevice(),
                    retryCount: 10,
                    delayProvider: retry => TimeSpan.FromMilliseconds(retry * 75),
                    shouldRetryOnException: ex => !(portGone = IsPortGone(ex)),
                    shouldRetryOnResult: null,
                    onRetry: LogRetry,
                    cancellationToken);

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
            catch (OperationCanceledException)
            {
                openDeviceResult = ConnectPortResult.NotConnected;
            }
            catch (Exception) when (portGone)
            {
                OnLogMessageAvailable(NanoDevicesEventSource.Log.PortGone(InstanceId));
                openDeviceResult = ConnectPortResult.NotConnected;
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
                if (openDeviceResult != ConnectPortResult.Connected)
                {
                    ReleaseExclusiveAccess(acquiredAccess);
                }
            }

            return openDeviceResult;
        }

        /// <summary>
        /// Checks if an error opening the port means that the port is gone (e.g. the device has been unplugged).
        /// </summary>
        /// <param name="exception">The error opening the port.</param>
        /// <returns><see langword="true"/> if the port is gone.</returns>
        /// <remarks>
        /// Only after the port has been opened once: right after a device is plugged in, the port can be reported
        /// before it's ready to be opened, and opening it fails as if it doesn't exist.
        /// </remarks>
        private bool IsPortGone(Exception exception)
        {
            if (!_hasBeenOpened)
            {
                // not ready yet, rather than gone: keep retrying
                return false;
            }

            switch (exception)
            {
                // .NET reports the missing device file
                case FileNotFoundException:
                    return true;

                // .NET Framework reports a generic IOException (localized message, no Win32 error code in the HResult):
                // have to check if the port is there
                case IOException:
                    return !PortExists();

                default:
                    return false;
            }
        }

        /// <summary>
        /// Checks if the serial port is still present in the system.
        /// </summary>
        /// <returns><see langword="false"/> if the port is gone (e.g. the device has been unplugged).
        /// <see langword="true"/> if it's present or if that can't be determined.</returns>
        private bool PortExists()
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    // the port name (e.g. COM31) is a link to the driver's device, removed as soon as the device is gone:
                    // opening the port fails with "port doesn't exist" when the link is missing.
                    // (the ports list in the registry, used by SerialPort.GetPortNames(), can still have it for a while)
                    var target = new StringBuilder(MaxDosDeviceTargetLength);

                    return QueryDosDevice(InstanceId, target, target.Capacity) != 0
                        || Marshal.GetLastWin32Error() != ERROR_FILE_NOT_FOUND;
                }

                // elsewhere the port is a device file (e.g. /dev/ttyACM0)
                return File.Exists(InstanceId);
            }
            catch
            {
                // can't tell: assume it's there
                return true;
            }
        }

        private const int ERROR_FILE_NOT_FOUND = 2;
        private const int MaxDosDeviceTargetLength = 260;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint QueryDosDevice(string lpDeviceName, StringBuilder lpTargetPath, int ucchMax);

        private void LogRetry(Exception response, TimeSpan delay, int retryCount)
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
