//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    public partial class PortSerial : PortMessageBase, IPort
    {
        private readonly PortSerialManager _portManager;

        public SerialPort Device { get; internal set; }

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
                return NanoDevice.Device.DeviceInformation.InstanceId;
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

                Device = new SerialPort(InstanceId, BaudRate, Parity.None, 8);

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
            if (IsDeviceConnected)
            {
                CloseCurrentlyConnectedDevice();
            }
        }

        /// <summary>
        /// Closes the device, stops the device watcher, stops listening for app events, and resets object state to before a device
        /// was ever connected.
        /// 
        /// When the SerialDevice is closing, it will cancel all IO operations that are still pending (not complete).
        /// The close will not wait for any IO completion callbacks to be called, so the close call may complete before any of
        /// the IO completion callbacks are called.
        /// The pending IO operations will still call their respective completion callbacks with either a task 
        /// cancelled error or the operation completed.
        /// </summary>
        private void CloseCurrentlyConnectedDevice()
        {
            if (Device != null)
            {
                Device.Close();
                Device = null;
            }
        }

        private void Device_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            //throw new NotImplementedException();
        }

        #endregion

        public bool ConnectDevice()
        {
            bool connectFlag = ConnectSerialDevice();

            if (connectFlag && NanoDevice.DeviceBase == null)
            {
                NanoDevice.DeviceBase = Device;
            }

            return connectFlag;
        }

        private bool ConnectSerialDevice()
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

        public void DisconnectDevice()
        {
            // disconnecting the current device

            //try
            //{
            OnLogMessageAvailable(NanoDevicesEventSource.Log.CloseDevice(InstanceId));

            // close device
            CloseDevice();

            // stop and dispose DebugEgine if instantiated
            NanoDevice.DebugEngine?.Stop();
            NanoDevice.DebugEngine?.Dispose();
            NanoDevice.DebugEngine = null;
            //}
            //catch
            //{
            //    // catch all required to deal with possible Exceptions when disconnecting the device
            //}
        }

        #region Interface implementations

        public DateTime LastActivity { get; set; }

        public int AvailableBytes => Device.BytesToRead;

        public int SendBuffer(byte[] buffer)
        {
            // device must be connected
            if (IsDeviceConnected)
            {
                // compute expected operation time

                int oldTimeout = 0;

                var operationTime = OperationTimeInMillisec(buffer.Length);

                if (Device.WriteTimeout != operationTime)
                {
                    oldTimeout = Device.WriteTimeout;

                    Device.WriteTimeout = operationTime;
                }

                try
                {
                    // write buffer to device
                    Device.Write(buffer, 0, buffer.Length);

                    return buffer.Length;
                }
                catch (TimeoutException)
                {
                    // this is expected to happen when the timeout occurs, no need to do anything with it
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"SendRawBufferAsync-Serial-Exception occurred: {ex.Message}\r\n {ex.StackTrace}");

                    throw;
                }
                finally
                {
                    // check if timeout needs to be restored
                    if(oldTimeout != 0)
                    {
                        Device.WriteTimeout = oldTimeout;
                    }
                }
            }
            else
            {
                throw new DeviceNotConnectedException();
            }

            return 0;
        }

        public byte[] ReadBuffer(int bytesToRead)
        {
            // device must be connected
            if (IsDeviceConnected)
            {
                int oldTimeout = 0;
                byte[] buffer = new byte[bytesToRead];

                var operationTime = OperationTimeInMillisec(bytesToRead);

                if (Device.ReadTimeout != operationTime)
                {
                    oldTimeout = Device.ReadTimeout;

                    Device.ReadTimeout = operationTime;
                }

                try
                {
                    Device.Read(buffer, 0, bytesToRead);

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
                finally
                {
                    // check if timeout needs to be restored
                    if (oldTimeout != 0)
                    {
                        Device.ReadTimeout = oldTimeout;
                    }
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

        private int OperationTimeInMillisec(int byteCount)
        {
            // minimum operation time 
            const int minimumOperationTime = 50;

            // get bit time
            double bitTime = (1.0 / BaudRate) * 1000;

            // time for all bytes plus stop bits
            // add a couple of extra bits, for just in case
            int opTime = (int)((byteCount * (8 + (int)Device.StopBits) + 2)  * bitTime);

            if (opTime < minimumOperationTime)
            {
                return minimumOperationTime;
            }
            else
            {
                return opTime;
            }
        }
    }
}
