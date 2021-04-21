//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    public partial class PortSerial : PortMessageBase, IPort
    {
        private readonly PortSerialManager _portManager;

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
        public bool OpenDevice()
        {
            bool successfullyOpenedDevice = false;
            bool retry = false;

            try
            {
                /////////////////////////////////////////////////////////////
                // need to FORCE the parity setting to _NONE_ because        
                // the default on the current ST Link is different causing 
                // the communication to fail
                /////////////////////////////////////////////////////////////

                NanoDevice.DeviceBase = new SerialPort(InstanceId, BaudRate, Parity.None, 8);

                // Device could have been blocked by user or the device has already been opened by another app.
                if (Device != null)
                {
                    try
                    {
                        Device.Open();
                    }
                    catch (IOException)
                    {
                        retry = true;
                    }

                    if (retry)
                    {
                        Thread.Sleep(100);
                        Device.Open();
                    }

                    successfullyOpenedDevice = true;

                    Device.WriteTimeout = 500;
                    Device.ReadTimeout = 500;
                    Device.ErrorReceived += Device_ErrorReceived;
                }
                else
                {
                    successfullyOpenedDevice = false;
                }
            }
#if DEBUG
            catch (Exception ex)
#else
            catch()
#endif
            {
                // catch all because the device open might fail for a number of reasons
            }

            return successfullyOpenedDevice;
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
                ((SerialPort)NanoDevice.DeviceBase).Close();
                ((SerialPort)NanoDevice.DeviceBase).Dispose();
            }
        }

        private void Device_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            Debug.WriteLine($">>>> Serial ERROR from {InstanceId}: {e.EventType}");
        }

        #endregion

        public bool ConnectDevice()
        {
            // try to determine if we already have this device opened.
            if (Device != null)
            {
                return true;
            }

            bool openDeviceResult = OpenDevice();

            if (openDeviceResult)
            {
                OnLogMessageAvailable(NanoDevicesEventSource.Log.OpenDevice(InstanceId));
            }
            else
            {
                // Most likely the device is opened by another app, but cannot be sure
                OnLogMessageAvailable(NanoDevicesEventSource.Log.CriticalError($"Unknown error opening {InstanceId}, possibly opened by another app"));
            }

            return openDeviceResult;
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
