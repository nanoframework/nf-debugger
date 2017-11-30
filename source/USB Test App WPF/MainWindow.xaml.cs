//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using nanoFramework.Tools.Debugger;
using nanoFramework.Tools.Debugger.Extensions;
using nanoFramework.Tools.Debugger.WireProtocol;
using Serial_Test_App_WPF.ViewModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace Serial_Test_App_WPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        private async void ConnectDeviceButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
            {
                var device = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex];

                bool connectResult = await device.DebugEngine.ConnectAsync(5000, true);

                //(DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.Start();

                var di = await (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].GetDeviceInfoAsync();

                Debug.WriteLine("");
                Debug.WriteLine("");
                Debug.WriteLine(di.ToString());
                Debug.WriteLine("");
                Debug.WriteLine("");

            }));

            // enable button
            (sender as Button).IsEnabled = true;

        }

        private async void PingButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () => {

                var p = await (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].PingAsync();

                Debug.WriteLine("");
                Debug.WriteLine("");
                Debug.WriteLine("Ping response: " + p.ToString());
                Debug.WriteLine("");
                Debug.WriteLine("");

            }));

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private object await(MainViewModel mainViewModel)
        {
            throw new NotImplementedException();
        }

        private  void DisconnectDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action( () => {

                try
                {
                    (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.Stop();
                    (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.Dispose();
                    (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine = null;
                    //Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                    //    ConnectionStateResult = ConnectionState.Disconnected;
                    //}));
                }
                catch (Exception ex)
                {

                }

            }));

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void GetExecutionModeButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
             {

                 try
                 {
                     var deviceState = await (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.GetExecutionModeAsync();

                     if (deviceState == Commands.DebuggingExecutionChangeConditions.State.Unknown)
                     {
                         Debug.WriteLine($">>> Couldn't determine device state <<<<");
                     }
                     else if (deviceState == Commands.DebuggingExecutionChangeConditions.State.Initialize)
                     {
                         Debug.WriteLine($">>> Device is in initialized state <<<<");
                     }
                     else if ((deviceState & Commands.DebuggingExecutionChangeConditions.State.ProgramRunning) == Commands.DebuggingExecutionChangeConditions.State.ProgramRunning)
                     {
                         if ((deviceState & Commands.DebuggingExecutionChangeConditions.State.Stopped) == Commands.DebuggingExecutionChangeConditions.State.Stopped)
                         {
                             Debug.WriteLine($">>> Device is running a program **BUT** execution is stopped <<<<");
                         }
                         else
                         {
                             Debug.WriteLine($">>> Device is running a program <<<<");
                         }
                     }
                     else if ((deviceState & Commands.DebuggingExecutionChangeConditions.State.ProgramExited) == Commands.DebuggingExecutionChangeConditions.State.ProgramExited)
                     {
                         Debug.WriteLine($">>> Device it's idle after exiting from a program execution <<<<");
                     }
                 }
                 catch (Exception ex)
                 {

                 }

             }));

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void ResolveAssembliesButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
             {

                 try
                 {
                     // Create cancelation token source
                     CancellationTokenSource cts = new CancellationTokenSource();

                     var result = await (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.ResolveAllAssembliesAsync(cts.Token);

                     Debug.WriteLine("Assembly list:");
                     
                     foreach (nanoFramework.Tools.Debugger.WireProtocol.Commands.DebuggingResolveAssembly assembly in result)
                     {
                         Debug.WriteLine($" {assembly.Idx} :: {assembly.Result.Name} [{assembly.Result.Path}]");
                     }

                    //Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                    //    ConnectionStateResult = ConnectionState.Disconnected;
                    //}));
                }
                 catch (Exception ex)
                 {

                 }

             }));

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void DeviceCapabilitesButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
             {

                 try
                 {
                     // Create cancellation token source
                     CancellationTokenSource cts = new CancellationTokenSource();

                     var device = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex];

                     // get device info
                     var deviceInfo = await device.GetDeviceInfoAsync(true);
                     var memoryMap = await device.DebugEngine.GetMemoryMapAsync();
                     var flashMap = await device.DebugEngine.GetFlashSectorMapAsync();
                     var deploymentMap = await device.DebugEngine.GetDeploymentMapAsync();

                     // we have to have a valid device info
                     if (deviceInfo.Valid)
                     {
                         // load vars
                         var deviceMemoryMap = new StringBuilder(memoryMap?.ToStringForOutput() ?? "Empty");
                         var deviceFlashSectorMap = new StringBuilder(flashMap?.ToStringForOutput() ?? "Empty");
                         var deviceDeploymentMap = new StringBuilder(deploymentMap?.ToStringForOutput() ?? "Empty");
                         var deviceSystemInfo = new StringBuilder(deviceInfo?.ToString() ?? "Empty");


                         Debug.WriteLine("System Information");
                         Debug.WriteLine(deviceSystemInfo.ToString());

                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine("--------------------------------");
                         Debug.WriteLine("::        Memory Map          ::");
                         Debug.WriteLine("--------------------------------");
                         Debug.WriteLine(deviceMemoryMap.ToString());

                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine("-----------------------------------------------------------");
                         Debug.WriteLine("::                   Flash Sector Map                    ::");
                         Debug.WriteLine("-----------------------------------------------------------");
                         Debug.WriteLine(deviceFlashSectorMap.ToString());

                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine("Deployment Map");
                         Debug.WriteLine(deviceDeploymentMap.ToString());

                     }
                     else
                     {
                         // invalid device info
                         Debug.WriteLine("");
                         Debug.WriteLine("Invalid device info");
                         Debug.WriteLine("");

                         return;
                     }
                }
                 catch (Exception ex)
                 {

                 }

             }));

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void DeployTestButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            List<byte[]> assemblies = new List<byte[]>();

            try
            {
                // add mscorlib
                string assemblyPath = @"..\..\..\packages\nanoFramework.CoreLibrary.1.0.0-preview022\lib\mscorlib.pe";

                using (FileStream fs = File.Open(assemblyPath, FileMode.Open, FileAccess.Read))
                {
                    Debug.WriteLine($"Adding pe file {assemblyPath} to deployment bundle");
                    long length = (fs.Length + 3) / 4 * 4;
                    byte[] buffer = new byte[length];

                    fs.Read(buffer, 0, (int)fs.Length);
                    assemblies.Add(buffer);
                }

                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
                {

                    var result = await (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.DeploymentExecuteAsync(assemblies, false);

                    Debug.WriteLine($">>> Deployment result: {result} <<<<");

                }));
            }
            catch (Exception ex)
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void FlashMapButton_Click(object sender, RoutedEventArgs e)
        {  
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    var fm = await (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.GetFlashSectorMapAsync();

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine(fm.ToStringForOutput());
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch (Exception ex)
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void ResumeExecutionButton_Click(object sender, RoutedEventArgs e)
        {  
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    var result = await (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.ResumeExecutionAsync();

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    if (result)
                    {
                        Debug.WriteLine("execution resumed");
                    }
                    else
                    {
                        Debug.WriteLine("couldn't resume execution");
                    }
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch (Exception ex)
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void PauseExecutionButton_Click(object sender, RoutedEventArgs e)
        {  
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    var result = await (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.PauseExecutionAsync();

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    if (result)
                    {
                        Debug.WriteLine("execution stopped");
                    }
                    else
                    {
                        Debug.WriteLine("couldn't stop execution");
                    }
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch (Exception ex)
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void RebootDeviceButton_Click(object sender, RoutedEventArgs e)
        {  
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    await (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.RebootDeviceAsync(nanoFramework.Tools.Debugger.RebootOption.RebootClrOnly);

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine($"CLR reboot");
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch (Exception ex)
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void SoftRebootDeviceButton_Click(object sender, RoutedEventArgs e)
        {  
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    await (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.RebootDeviceAsync(nanoFramework.Tools.Debugger.RebootOption.NormalReboot);

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine($"soft reboot");
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch (Exception ex)
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void RebootAndDebugDeviceButton_Click(object sender, RoutedEventArgs e)
        {  
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    await (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.RebootDeviceAsync(nanoFramework.Tools.Debugger.RebootOption.RebootClrWaitForDebugger);

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine($"CLR reboot");
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch (Exception ex)
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void GetDeploymentMapButton_Click(object sender, RoutedEventArgs e)
        {  
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    var dm = await (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.GetDeploymentMapAsync();

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine(dm?.ToStringForOutput());
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch (Exception ex)
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void StopProcessingButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.Stop();

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine($"Start background processing");
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch (Exception ex)
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }
       
    }
}
