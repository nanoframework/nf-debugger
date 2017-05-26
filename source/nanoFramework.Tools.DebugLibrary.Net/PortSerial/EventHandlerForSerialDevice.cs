//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.SerialCommunication;
using Windows.Foundation;
using System.Windows;
using Windows.UI.Xaml;

namespace nanoFramework.Tools.Debugger.Serial
{
    /// <summary>
    /// This class handles the required changes and operation of an SerialDevice when a specific app event
    /// is raised (app suspension and resume) or when the device is disconnected. The device watcher events are also handled here.
    /// </summary>
    public partial class EventHandlerForSerialDevice
    {
        private EventHandler appSuspendEventHandler;
        private EventHandler appResumeEventHandler;

        private SuspendingEventHandler appSuspendCallback;

        // A pointer back to the calling app.  This is needed to reach methods and events there 
        private static System.Windows.Application _callerApp;
        public static System.Windows.Application CallerApp
        {
            private get { return _callerApp; }
            set { _callerApp = value; }
        }

        /// <summary>
        /// Register for app suspension/resume events. See the comments
        /// for the event handlers for more information on what is being done to the device.
        ///
        /// We will also register for when the app exists so that we may close the device handle.
        /// </summary>
        private void RegisterForAppEvents()
        {
            appSuspendEventHandler = new EventHandler(Current.OnAppDeactivated);
            appResumeEventHandler = new EventHandler(Current.OnAppResume);

            // This event is raised when the app is exited and when the app is suspended
            _callerApp.Deactivated += _callerApp_Deactivated;

            _callerApp.Activated += appResumeEventHandler;
        }

        private void _callerApp_Deactivated(object sender, EventArgs e)
        {
            throw new NotImplementedException();
        }

        private void UnregisterFromAppEvents()
        {
            // This event is raised when the app is exited and when the app is suspended
            _callerApp.Deactivated -= appSuspendEventHandler;
            appSuspendEventHandler = null;

            _callerApp.Activated -= appResumeEventHandler;
            appResumeEventHandler = null;
        }

        /// <summary>
        /// If a SerialDevice object has been instantiated (a handle to the device is opened), we must close it before the app 
        /// goes into suspension because the API automatically closes it for us if we don't. When resuming, the API will
        /// not reopen the device automatically, so we need to explicitly open the device in that situation.
        ///
        /// Since we have to reopen the device ourselves when the app resumes, it is good practice to explicitly call the close
        /// in the app as well (For every open there is a close).
        /// 
        /// We must stop the DeviceWatcher because it will continue to raise events even if
        /// the app is in suspension, which is not desired (drains battery). We resume the device watcher once the app resumes again.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="eventArgs"></param>
        private void OnAppDeactivated(object sender, EventArgs args)
        {
            if (watcherStarted)
            {
                watcherSuspended = true;
                StopDeviceWatcher();
            }
            else
            {
                watcherSuspended = false;
            }

            //// Forward suspend event to registered callback function
            //if (appSuspendCallback != null)
            //{
            //    appSuspendCallback(sender, args);
            //}

            CloseCurrentlyConnectedDevice();
        }
    }
}
