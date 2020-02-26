//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Storage.Streams;

namespace nanoFramework.Tools.Debugger.PortSerial
{
    public partial class SerialPort : PortMessageBase, IPort
    {
        // R/W operation cancellation tokens objects
        private CancellationTokenSource _readCancellationTokenSource;
        private CancellationTokenSource _sendCancellationTokenSource;
        private readonly object _readCancelLock = new object();
        private readonly object _sendCancelLock = new object();
        private readonly SerialPortManager _portManager;

        public SerialDevice Device { get; internal set; }

        // valid baud rates
        public static readonly List<uint> ValidBaudRates = new List<uint>() { 921600, 460800, 115200 };

        public uint BaudRate { get; internal set; }

        public NanoDevice<NanoSerialDevice> NanoDevice { get; }

        /// <summary>
        /// This DeviceInformation represents which device is connected or which device will be reconnected when
        /// the device is plugged in again (if IsEnabledAutoReconnect is true);.
        /// </summary>
        public DeviceInformation DeviceInformation
        {
            get
            {
                return NanoDevice.Device.DeviceInformation.DeviceInformation;
            }
        }

        /// <summary>
        /// Returns DeviceAccessInformation for the device that is currently connected using this EventHandlerForSerialEclo
        /// object.
        /// </summary>
        public DeviceAccessInformation DeviceAccessInformation { get; }

        /// <summary>
        /// Creates an Serial debug client
        /// </summary>
        /// <param name="deviceInfo">Device information of the device to be opened</param>
        /// <param name="deviceSelector">The AQS used to find this device</param>
        public SerialPort(SerialPortManager portManager, NanoDevice<NanoSerialDevice> serialDevice)
        {
            _portManager = portManager ?? throw new ArgumentNullException(nameof(portManager));
            NanoDevice = serialDevice ?? throw new ArgumentNullException(nameof(serialDevice));

            // init default baud rate with 1st value
            BaudRate =  ValidBaudRates[0];

            ResetReadCancellationTokenSource();
            ResetSendCancellationTokenSource();
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
        public async Task<bool> OpenDeviceAsync()
        {
            bool successfullyOpenedDevice = false;

            try
            {
                // need to wrap the call to FromIdAsync on a task with a timed cancellation token to force a constrained execution time 
                // as this API call can block execution when an exception occurs inside it (the real reason is undetermined, seems to be with the driver) 
                // has reportedly been seen with Bluetooth virtual serial ports and some ESP32 serial interfaces

                var cts = new CancellationTokenSource();
                cts.CancelAfter(1000);

                Device = await SerialDevice.FromIdAsync(DeviceInformation.Id).AsTask(cts.Token).CancelAfterAsync(1000, cts);

                // Device could have been blocked by user or the device has already been opened by another app.
                if (Device != null)
                {
                    successfullyOpenedDevice = true;

                    // adjust settings for serial port
                    // baud rate is coming from the property
                    Device.BaudRate = BaudRate;
                    Device.DataBits = 8;

                    /////////////////////////////////////////////////////////////
                    // need to FORCE the parity setting to _NONE_ because        
                    // the default on the current ST Link is different causing 
                    // the communication to fail
                    /////////////////////////////////////////////////////////////
                    Device.Parity = SerialParity.None;

                    Device.WriteTimeout = TimeSpan.FromMilliseconds(500);
                    Device.ReadTimeout = TimeSpan.FromMilliseconds(1000);
                    Device.ErrorReceived += Device_ErrorReceived;

                    //// Background tasks are not part of the app, so app events will not have an affect on the device
                    //if (!_isBackgroundTask && (_appSuspendEventHandler == null || _appResumeEventHandler == null))
                    //{
                    //    RegisterForAppEvents();
                    //}

                    //// User can block the device after it has been opened in the Settings charm. We can detect this by registering for the 
                    //// DeviceAccessInformation.AccessChanged event
                    //if (_deviceAccessEventHandler == null)
                    //{
                    //    RegisterForDeviceAccessStatusChange();
                    //}

                    //// Create and register device watcher events for the device to be opened unless we're reopening the device
                    //if (_deviceWatcher == null)
                    //{
                    //    _deviceWatcher = DeviceInformation.CreateWatcher(deviceSelector);

                    //    RegisterForDeviceWatcherEvents();
                    //}

                    //if (!_watcherStarted)
                    //{
                    //    // Start the device watcher after we made sure that the device is opened.
                    //    StartDeviceWatcher();
                    //}
                }
                else
                {
                    successfullyOpenedDevice = false;
                }
            }
            catch
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
                // dispose on a Task to give it a timeout to perform the Dispose()
                // this is required to be able to actually close devices that get stuck with pending tasks on the in/output streams
                var closeTask = Task.Factory.StartNew(() =>
                {
                    try
                    {
                        // This closes the handle to the device
                        Device?.Dispose();
                        Device = null;
                    }
                    catch
                    {
                        // catch all required to deal with possible InteropServices.SEHException
                    }
                });

                //need to wrap this in try-catch to catch possible AggregateExceptions
                try
                {
                    Task.WaitAll(new Task[] { closeTask }, TimeSpan.FromMilliseconds(1000));
                }
                catch
                {
                    // catch all required to deal with possible AggregateExceptions
                }
            }
        }

        private void Device_ErrorReceived(SerialDevice sender, ErrorReceivedEventArgs args)
        {
            //throw new NotImplementedException();
        }

        #endregion

        public async Task<bool> ConnectDeviceAsync()
        {
            bool connectFlag = await ConnectSerialDeviceAsync();

            if(connectFlag && NanoDevice.DeviceBase == null)
            {
                NanoDevice.DeviceBase = Device;
            }

            return connectFlag;
        }

        private async Task<bool> ConnectSerialDeviceAsync()
        {
            // try to determine if we already have this device opened.
            if (Device != null)
            {
                return true;
            }

            bool openDeviceResult = await OpenDeviceAsync();

            if (openDeviceResult)
            {
                OnLogMessageAvailable(NanoDevicesEventSource.Log.OpenDevice(DeviceInformation.Id));
            }
            else
            {
                // Most likely the device is opened by another app, but cannot be sure
                OnLogMessageAvailable(NanoDevicesEventSource.Log.CriticalError($"Unknown error opening {DeviceInformation.Id}, possibly opened by another app"));
            }

            return openDeviceResult;
        }

        public void DisconnectDevice()
        {
            // disconnecting the current device

            try
            {
                // cancel all IO operations
                CancelAllIoTasks();

                OnLogMessageAvailable(NanoDevicesEventSource.Log.CloseDevice(DeviceInformation.Id));

                // close device
                CloseDevice();

                // stop and dispose DebugEgine if instantiated
                NanoDevice.DebugEngine?.Stop();
                NanoDevice.DebugEngine?.Dispose();
                NanoDevice.DebugEngine = null;
            }
            catch
            {
                // catch all required to deal with possible Exceptions when disconnecting the device
            }
        }

        private void CancelReadTask()
        {
            lock (_readCancelLock)
            {
                if (_readCancellationTokenSource != null)
                {
                    if (!_readCancellationTokenSource.IsCancellationRequested)
                    {
                        _readCancellationTokenSource.Cancel();

                        // Existing IO already has a local copy of the old cancellation token so this reset won't affect it
                        ResetReadCancellationTokenSource();
                    }
                }
            }
        }

        private void CancelWriteTask()
        {
            lock (_sendCancelLock)
            {
                if (_sendCancellationTokenSource != null)
                {
                    if (!_sendCancellationTokenSource.IsCancellationRequested)
                    {
                        _sendCancellationTokenSource.Cancel();

                        // Existing IO already has a local copy of the old cancellation token so this reset won't affect it
                        ResetSendCancellationTokenSource();
                    }
                }
            }
        }

        private void CancelAllIoTasks()
        {
            CancelReadTask();
            CancelWriteTask();
        }

        private void ResetReadCancellationTokenSource()
        {
            // Create a new cancellation token source so that can cancel all the tokens again
            _readCancellationTokenSource = new CancellationTokenSource();

            // Hook the cancellation callback (called whenever Task.cancel is called)
            // TODO this probably could be used to notify the debug engine and others of the cancellation
            //ReadCancellationTokenSource.Token.Register(() => NotifyReadCancelingTask());
        }

        private void ResetSendCancellationTokenSource()
        {
            // Create a new cancellation token source so that can cancel all the tokens again
            _sendCancellationTokenSource = new CancellationTokenSource();

            // Hook the cancellation callback (called whenever Task.cancel is called)
            // TODO this probably could be used to notify the debug engine and others of the cancellation
            //SendCancellationTokenSource.Token.Register(() => NotifySendCancelingTask());
        }

        #region Interface implementations

        public DateTime LastActivity { get; set; }

        public async Task<uint> SendBufferAsync(byte[] buffer, TimeSpan waiTimeout, CancellationToken cancellationToken)
        {
            // device must be connected
            if (IsDeviceConnected && !cancellationToken.IsCancellationRequested)
            {
                DataWriter outputStreamWriter = new DataWriter(Device.OutputStream);
                Task<UInt32> storeAsyncTask;


                using (CancellationTokenSource linkedCts =
                        CancellationTokenSource.CreateLinkedTokenSource(_sendCancellationTokenSource.Token, cancellationToken))
                {
                    try
                    {
                        // write buffer to device
                        outputStreamWriter.WriteBytes(buffer);

                        // Don't start any IO if the task was cancelled
                        lock (_sendCancelLock)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            linkedCts.Token.ThrowIfCancellationRequested();

                            storeAsyncTask = outputStreamWriter.StoreAsync().AsTask(linkedCts.Token.AddTimeout(waiTimeout));
                        }

                        return await storeAsyncTask;
                    }
                    catch (TaskCanceledException)
                    {
                        // this is expected to happen when the timeout occurs, no need to do anything with it
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"SendRawBufferAsync-Serial-Exception occurred: {ex.Message}\r\n {ex.StackTrace}");

                        // something very wrong happened, disconnect immediately
                        DisconnectDevice();

                        return 0;
                    }
                    finally
                    {
                        // detach stream
                        outputStreamWriter?.DetachStream();
                    }
                }
            }
            else
            {
                throw new DeviceNotConnectedException();
            }

            return 0;
        }

        public async Task<byte[]> ReadBufferAsync(uint bytesToRead, TimeSpan waiTimeout, CancellationToken cancellationToken)
        {
            // device must be connected
            if (IsDeviceConnected && !cancellationToken.IsCancellationRequested)
            {
                DataReader inputStreamReader = new DataReader(Device.InputStream);
                Task<UInt32> loadAsyncTask;

                using (CancellationTokenSource linkedCts =
                        CancellationTokenSource.CreateLinkedTokenSource(_readCancellationTokenSource.Token, cancellationToken))
                {

                    try
                    {
                        // Don't start any IO if the task was cancelled
                        lock (_readCancelLock)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            linkedCts.Token.ThrowIfCancellationRequested();

                            loadAsyncTask = inputStreamReader.LoadAsync(bytesToRead).AsTask(linkedCts.Token.AddTimeout(waiTimeout));
                        }

                        UInt32 bytesRead = await loadAsyncTask;

                        if (bytesRead > 0)
                        {
                            byte[] readBuffer = new byte[bytesRead];
                            inputStreamReader?.ReadBytes(readBuffer);

                            return readBuffer;
                        }
                    }
                    catch (TaskCanceledException)
                    {
                        // this is expected to happen when the timeout occurs, no need to do anything with it
                    }
                    catch (NullReferenceException)
                    {
                        // this is expected to happen when there isn't anything to read, don't bother with it
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"ReadBufferAsync-Serial-Exception occurred: {ex.Message}\r\n {ex.StackTrace}");

                        // something very wrong happened, disconnect immediately
                        DisconnectDevice();

                        return new byte[0];
                    }
                    finally
                    {
                        // detach stream
                        inputStreamReader?.DetachStream();
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
    }
}
