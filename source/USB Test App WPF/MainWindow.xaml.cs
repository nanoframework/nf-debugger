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
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
        // Baltimore CyberTrust Root
        // from https://cacert.omniroot.com/bc2025.crt

        // X509 RSA key PEM format 2048 bytes
        private const string baltimoreCACertificate =
@"-----BEGIN CERTIFICATE-----
MIIDdzCCAl+gAwIBAgIEAgAAuTANBgkqhkiG9w0BAQUFADBaMQswCQYDVQQGEwJJ
RTESMBAGA1UEChMJQmFsdGltb3JlMRMwEQYDVQQLEwpDeWJlclRydXN0MSIwIAYD
VQQDExlCYWx0aW1vcmUgQ3liZXJUcnVzdCBSb290MB4XDTAwMDUxMjE4NDYwMFoX
DTI1MDUxMjIzNTkwMFowWjELMAkGA1UEBhMCSUUxEjAQBgNVBAoTCUJhbHRpbW9y
ZTETMBEGA1UECxMKQ3liZXJUcnVzdDEiMCAGA1UEAxMZQmFsdGltb3JlIEN5YmVy
VHJ1c3QgUm9vdDCCASIwDQYJKoZIhvcNAQEBBQADggEPADCCAQoCggEBAKMEuyKr
mD1X6CZymrV51Cni4eiVgLGw41uOKymaZN+hXe2wCQVt2yguzmKiYv60iNoS6zjr
IZ3AQSsBUnuId9Mcj8e6uYi1agnnc+gRQKfRzMpijS3ljwumUNKoUMMo6vWrJYeK
mpYcqWe4PwzV9/lSEy/CG9VwcPCPwBLKBsua4dnKM3p31vjsufFoREJIE9LAwqSu
XmD+tqYF/LTdB1kC1FkYmGP1pWPgkAx9XbIGevOF6uvUA65ehD5f/xXtabz5OTZy
dc93Uk3zyZAsuT3lySNTPx8kmCFcB5kpvcY67Oduhjprl3RjM71oGDHweI12v/ye
jl0qhqdNkNwnGjkCAwEAAaNFMEMwHQYDVR0OBBYEFOWdWTCCR1jMrPoIVDaGezq1
BE3wMBIGA1UdEwEB/wQIMAYBAf8CAQMwDgYDVR0PAQH/BAQDAgEGMA0GCSqGSIb3
DQEBBQUAA4IBAQCFDF2O5G9RaEIFoN27TyclhAO992T9Ldcw46QQF+vaKSm2eT92
9hkTI7gQCvlYpNRhcL0EYWoSihfVCr3FvDB81ukMJY2GQE/szKN+OMY3EU/t3Wgx
jkzSswF07r51XgdIGn9w/xZchMB5hbgF/X++ZRGjD8ACtPhSNzkE1akxehi/oCr0
Epn3o0WC4zxe9Z2etciefC7IpJ5OCBRLbf1wbWsaY71k5h+3zvDyny67G7fyUIhz
ksLi4xaNmjICq44Y3ekQEe5+NauQrz4wlHrQMz2nZQ/1/I6eYs9HRCwBXbsdtTLS
R9I4LtD+gdwyah617jzV/OeBHRnDJELqYzmp
-----END CERTIFICATE-----";

        // Let’s Encrypt Authority X3 (IdenTrust cross-signed)
        // from https://letsencrypt.org/certificates/

        // X509 RSA key PEM format 2048 bytes
        private const string letsEncryptCACertificate =
@"-----BEGIN CERTIFICATE-----
MIIFjTCCA3WgAwIBAgIRANOxciY0IzLc9AUoUSrsnGowDQYJKoZIhvcNAQELBQAw
TzELMAkGA1UEBhMCVVMxKTAnBgNVBAoTIEludGVybmV0IFNlY3VyaXR5IFJlc2Vh
cmNoIEdyb3VwMRUwEwYDVQQDEwxJU1JHIFJvb3QgWDEwHhcNMTYxMDA2MTU0MzU1
WhcNMjExMDA2MTU0MzU1WjBKMQswCQYDVQQGEwJVUzEWMBQGA1UEChMNTGV0J3Mg
RW5jcnlwdDEjMCEGA1UEAxMaTGV0J3MgRW5jcnlwdCBBdXRob3JpdHkgWDMwggEi
MA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQCc0wzwWuUuR7dyXTeDs2hjMOrX
NSYZJeG9vjXxcJIvt7hLQQWrqZ41CFjssSrEaIcLo+N15Obzp2JxunmBYB/XkZqf
89B4Z3HIaQ6Vkc/+5pnpYDxIzH7KTXcSJJ1HG1rrueweNwAcnKx7pwXqzkrrvUHl
Npi5y/1tPJZo3yMqQpAMhnRnyH+lmrhSYRQTP2XpgofL2/oOVvaGifOFP5eGr7Dc
Gu9rDZUWfcQroGWymQQ2dYBrrErzG5BJeC+ilk8qICUpBMZ0wNAxzY8xOJUWuqgz
uEPxsR/DMH+ieTETPS02+OP88jNquTkxxa/EjQ0dZBYzqvqEKbbUC8DYfcOTAgMB
AAGjggFnMIIBYzAOBgNVHQ8BAf8EBAMCAYYwEgYDVR0TAQH/BAgwBgEB/wIBADBU
BgNVHSAETTBLMAgGBmeBDAECATA/BgsrBgEEAYLfEwEBATAwMC4GCCsGAQUFBwIB
FiJodHRwOi8vY3BzLnJvb3QteDEubGV0c2VuY3J5cHQub3JnMB0GA1UdDgQWBBSo
SmpjBH3duubRObemRWXv86jsoTAzBgNVHR8ELDAqMCigJqAkhiJodHRwOi8vY3Js
LnJvb3QteDEubGV0c2VuY3J5cHQub3JnMHIGCCsGAQUFBwEBBGYwZDAwBggrBgEF
BQcwAYYkaHR0cDovL29jc3Aucm9vdC14MS5sZXRzZW5jcnlwdC5vcmcvMDAGCCsG
AQUFBzAChiRodHRwOi8vY2VydC5yb290LXgxLmxldHNlbmNyeXB0Lm9yZy8wHwYD
VR0jBBgwFoAUebRZ5nu25eQBc4AIiMgaWPbpm24wDQYJKoZIhvcNAQELBQADggIB
ABnPdSA0LTqmRf/Q1eaM2jLonG4bQdEnqOJQ8nCqxOeTRrToEKtwT++36gTSlBGx
A/5dut82jJQ2jxN8RI8L9QFXrWi4xXnA2EqA10yjHiR6H9cj6MFiOnb5In1eWsRM
UM2v3e9tNsCAgBukPHAg1lQh07rvFKm/Bz9BCjaxorALINUfZ9DD64j2igLIxle2
DPxW8dI/F2loHMjXZjqG8RkqZUdoxtID5+90FgsGIfkMpqgRS05f4zPbCEHqCXl1
eO5HyELTgcVlLXXQDgAWnRzut1hFJeczY1tjQQno6f6s+nMydLN26WuU4s3UYvOu
OsUxRlJu7TSRHqDC3lSE5XggVkzdaPkuKGQbGpny+01/47hfXXNB7HntWNZ6N2Vw
p7G6OfY+YQrZwIaQmhrIqJZuigsrbe3W+gdn5ykE9+Ky0VgVUsfxo52mwFYs1JKY
2PGDuWx8M6DlS6qQkvHaRUo0FMd8TsSlbF0/v965qGFKhSDeQoMpYnwcmQilRh/0
ayLThlHLN81gSkJjVrPI0Y8xCVPB4twb1PFUd2fPM3sA1tJ83sZ5v8vgFv2yofKR
PB0t6JzUA81mSqM3kxl5e+IZwhYAyO0OTg3/fs8HqGTNKd9BqoUwSRBzp06JMg5b
rUCGwbCUDI0mxadJ3Bz4WxR6fyNpBK2yAinWEsikxqEt
-----END CERTIFICATE-----";


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

                if(device.DebugEngine == null)
                {
                    device.CreateDebugEngine();
                }

                bool connectResult = await device.DebugEngine.ConnectAsync(5000, true);

                //(DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.Start();
                if(connectResult)
                {
                    device.DebugEngine.OnProcessExit += DebugEngine_OnProcessExit;
                }

                var di = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].GetDeviceInfo();

                Debug.WriteLine("");
                Debug.WriteLine("");
                Debug.WriteLine(di.ToString());
                Debug.WriteLine("");
                Debug.WriteLine("");

            }));

            // enable button
            (sender as Button).IsEnabled = true;

        }

        private void DebugEngine_OnProcessExit(object sender, EventArgs e)
        {
            Engine engine = sender as Engine;
            engine.Dispose();
        }

        private async void PingButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {

                var p = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].Ping();

                Debug.WriteLine("");
                Debug.WriteLine("");
                Debug.WriteLine("Ping response: " + p.ToString());
                Debug.WriteLine("");
                Debug.WriteLine("");

            }));

            // enable button
            (sender as Button).IsEnabled = true;
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

                    (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].Disconnect();
                }
                catch
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

            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {

                 try
                 {
                     var deviceState = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.GetExecutionMode();

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
                         if ((deviceState & Commands.DebuggingExecutionChangeConditions.State.ResolutionFailed) == Commands.DebuggingExecutionChangeConditions.State.ResolutionFailed)
                         {
                             Debug.WriteLine($">>> Device can't start execution because type resolution has failed <<<<");
                         }
                         else
                         {
                             Debug.WriteLine($">>> Device it's idle after exiting from a program execution <<<<");
                         }
                     }

                 }
                 catch
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

            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
             {

                 try
                 {
                     var result = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.ResolveAllAssemblies();

                     Debug.WriteLine("Assembly list:");
                     
                     foreach (Commands.DebuggingResolveAssembly assembly in result)
                     {
                         Debug.WriteLine($" {assembly.Idx} :: {assembly.Result.Name} [{assembly.Result.Path}]");
                     }

                    //Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {
                    //    ConnectionStateResult = ConnectionState.Disconnected;
                    //}));
                }
                 catch
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

            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
             {

                 try
                 {
                     // Create cancellation token source
                     CancellationTokenSource cts = new CancellationTokenSource();

                     var device = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex];

                     // get device info
                     var deviceInfo = device.GetDeviceInfo(true);
                     var memoryMap = device.DebugEngine.GetMemoryMap();
                     var flashMap = device.DebugEngine.GetFlashSectorMap();
                     var deploymentMap = device.DebugEngine.GetDeploymentMap();

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
                         Debug.WriteLine(deviceMemoryMap.ToString());

                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine(deviceFlashSectorMap.ToString());

                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine(deviceDeploymentMap.ToString());

                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine($"Target capabilities: { deviceInfo.TargetCapabilities }");

                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine($"Platform capabilities: { deviceInfo.PlatformCapabilities }");

                         Debug.WriteLine(string.Empty);
                         Debug.WriteLine(string.Empty);
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
                 catch
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

            List<byte[]> assemblies = new List<byte[]>(4);
            // test data (equivalent to deploying the Blinky test app)
            // mscorlib v1.0.0.0 (38448 bytes)
            // nanoFramework.Runtime.Events v1.0.0.0 (2568 bytes)
            // Windows.Devices.Gpio v1.0.0.0 (3800 bytes)
            // Blinky v1.0.0.0 (752 bytes)
            // assemblies to device...total size in bytes is 47032.

            var p1Size = 38448;
            var p2Size = 2568;
            var p3Size = 3800;
            var p4Size = 752;

            assemblies.Add(new byte[p1Size]);
            assemblies[0][0] = 0x5;
            assemblies[0][1] = 0x5;
            assemblies.Add(new byte[p2Size]);
            assemblies[1][0] = 0x6;
            assemblies[1][1] = 0x6;
            assemblies.Add(new byte[p3Size]);
            assemblies[2][0] = 0x7;
            assemblies[2][1] = 0x7;
            assemblies.Add(new byte[p4Size]);
            assemblies[3][0] = 0x8;
            assemblies[3][1] = 0x8;

            var totalSize = p1Size + p2Size + p3Size + p4Size;

            try
            {
                //// add mscorlib
                //string assemblyPath = @"..\..\..\packages\nanoFramework.CoreLibrary.1.0.0-preview022\lib\mscorlib.pe";

                //using (FileStream fs = File.Open(assemblyPath, FileMode.Open, FileAccess.Read))
                //{
                //    Debug.WriteLine($"Adding pe file {assemblyPath} to deployment bundle");
                //    long length = (fs.Length + 3) / 4 * 4;
                //    byte[] buffer = new byte[length];

                //    fs.Read(buffer, 0, (int)fs.Length);
                //    assemblies.Add(buffer);
                //}

                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    var debugEngine = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine;

                    var largePackets = totalSize / (debugEngine.WireProtocolPacketSize - 8);

                    var packetSize = debugEngine.WireProtocolPacketSize == 1024 ? "1k" : $"({ debugEngine.WireProtocolPacketSize / 1024}bytes";

                    Debug.WriteLine($">>> Sending : {totalSize} bytes.<<<<");
                    Debug.WriteLine($">>> This is {packetSize} packets plus something bytes.<<<<");

                    var result = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.DeploymentExecute(assemblies, true);

                    Debug.WriteLine($">>> Deployment result: {result} <<<<");

                    if (result)
                    {
                        //(DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.RebootDevice(RebootOptions.ClrOnly);

                        //Task.Delay(1000).Wait();

                        (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].GetDeviceInfo(true);
                    }

                }));
            }
            catch
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
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    var fm = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.GetFlashSectorMap();

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine(fm.ToStringForOutput());
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch
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
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    var result = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.ResumeExecution();

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
            catch
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
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    var result = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.PauseExecution();

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
            catch
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
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.RebootDevice(RebootOptions.ClrOnly);

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine($"CLR reboot");
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch
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
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.RebootDevice(RebootOptions.NormalReboot);

                    (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.Stop();
                    (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.Dispose();
                    (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine = null;

                    (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].Disconnect();

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine($"soft reboot");
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch
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
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.RebootDevice(RebootOptions.ClrOnly | RebootOptions.WaitForDebugger);

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine($"CLR reboot & wait for debugger");
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch
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
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    var dm = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.GetDeploymentMap();

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine(dm?.ToStringForOutput());
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch
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
                    Debug.WriteLine($"Stop background processing");
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch
            { 

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void EraseDeploymentButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    await (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].EraseAsync(EraseOptions.Deployment, CancellationToken.None);

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine($"Erased deployment area.");
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch
            { 

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void IsInitStateButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    var isInitState = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.IsDeviceInInitializeState();

                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine($"device is in init state: {isInitState}");
                    Debug.WriteLine("");
                    Debug.WriteLine("");

                }));
            }
            catch
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void GetDeviceConfigButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
             {

                 try
                 {
                     // Create cancellation token source
                     CancellationTokenSource cts = new CancellationTokenSource();

                     var device = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex];

                     // get device info
                     var deviceConfig = device.DebugEngine.GetDeviceConfiguration(cts.Token);


                    Debug.WriteLine(deviceConfig.NetworkConfigurations[0].ToStringForOutput());

                    //Debug.WriteLine(string.Empty);
                    //Debug.WriteLine(string.Empty);
                    //Debug.WriteLine("--------------------------------");
                    //Debug.WriteLine("::        Memory Map          ::");
                    //Debug.WriteLine("--------------------------------");


                 }
                 catch(Exception ex)
                 {

                 }

             }));

            // enable button
            (sender as Button).IsEnabled = true;
        }

        /// <summary>
        /// 
        /// 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <remarks>
        /// </remarks>
        private async void SetDeviceConfigButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
             {

                 try
                 {
                     // Create cancellation token source
                     CancellationTokenSource cts = new CancellationTokenSource();

                     var device = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex];

                     // get device info
                     var deviceConfig = device.DebugEngine.GetDeviceConfiguration(cts.Token);

                     // update new network configuration
                     DeviceConfiguration.NetworkConfigurationProperties newDeviceNetworkConfiguration = new DeviceConfiguration.NetworkConfigurationProperties
                     {
                         MacAddress = new byte[] { 0, 0x80, 0xe1, 0x01, 0x35, 0x56 },
                         InterfaceType = NetworkInterfaceType.Ethernet,
                         StartupAddressMode = AddressMode.DHCP,

                         IPv4DNSAddress1 = IPAddress.Parse("192.168.1.254"),
                     };

                     // write device configuration to device
                     var returnValue = device.DebugEngine.UpdateDeviceConfiguration(newDeviceNetworkConfiguration, 0);

                     //// add new wireless 802.11 configuration
                     //DeviceConfiguration.Wireless80211ConfigurationProperties newWireless80211Configuration = new DeviceConfiguration.Wireless80211ConfigurationProperties()
                     //{
                     //    Id = 44,
                     //    Ssid = "Nice_Ssid",
                     //    Password = "1234",
                     //};

                     //// write wireless configuration to device
                     //returnValue = device.DebugEngine.UpdateDeviceConfiguration(newWireless80211Configuration, 0);

                     // build a CA certificate bundle
                     DeviceConfiguration.X509CaRootBundleProperties newX509CertificateBundle = new DeviceConfiguration.X509CaRootBundleProperties();

                     // add CA root certificates

                     /////////////////////////////////////////////////////////
                     // BECAUSE WE ARE PARSING FROM A BASE64 encoded format //
                     // NEED TO ADD A TERMINATOR TO THE STRING              //
                     /////////////////////////////////////////////////////////

                     string caRootBundle = baltimoreCACertificate + letsEncryptCACertificate + "\0";

                     byte[] certificateRaw = Encoding.UTF8.GetBytes(caRootBundle);

                     newX509CertificateBundle.Certificate = certificateRaw;

                     // write CA certificate to device
                     returnValue = device.DebugEngine.UpdateDeviceConfiguration(newX509CertificateBundle, 0);

                     Debug.WriteLine("");
                     Debug.WriteLine("");
                     Debug.WriteLine($"device config update result: {returnValue}");
                     Debug.WriteLine("");
                     Debug.WriteLine("");

                 }
                 catch (Exception ex)
                 {

                 }

             }));

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void ReScanDevices_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {

                (DataContext as MainViewModel).SerialDebugService.SerialDebugClient.ReScanDevices();

            }));

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void ReadTestButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                // Create cancellation token source
                CancellationTokenSource cts = new CancellationTokenSource();

                var device = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex];

                // get memory map
                var memoryMap = device.DebugEngine.GetMemoryMap();

                // get flash map
                var flashSectorMap = device.DebugEngine.GetFlashSectorMap();

                // setup array for binary output
                byte[] binaryOutput = new byte[memoryMap.First(m => (m.m_flags & Commands.Monitor_MemoryMap.c_FLASH) == Commands.Monitor_MemoryMap.c_FLASH).m_length];
                var flashStartAddress = memoryMap.First(m => (m.m_flags & Commands.Monitor_MemoryMap.c_FLASH) == Commands.Monitor_MemoryMap.c_FLASH).m_address;

                // bootloader
                if (flashSectorMap.Exists(item => (item.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_BOOTSTRAP))
                {
                    var startAddress = flashSectorMap.First(item => (item.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_BOOTSTRAP).m_StartAddress;
                    var length = (uint)flashSectorMap.Where(item => (item.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_BOOTSTRAP).Sum(obj => obj.m_NumBlocks * obj.m_BytesPerBlock);

                    var bootloaderOperation = device.DebugEngine.ReadMemory(startAddress, length);
                    if(bootloaderOperation.Success)
                    {
                        // copy to array
                        Array.Copy(bootloaderOperation.Buffer, 0, binaryOutput, startAddress - flashStartAddress, length);
                    }
                    else
                    {
                        // check error code
                    }
                }

                // configuration
                //var configSector = flashSectorMap.Where(s => ((s.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CONFIG) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CONFIG)).Select(s => s.ToDeploymentSector()).ToList();
                
                // CLR
                if (flashSectorMap.Exists(item => (item.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE))
                {
                    var startAddress = flashSectorMap.First(item => (item.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE).m_StartAddress;
                    var length = (uint)flashSectorMap.Where(item => (item.m_flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE).Sum(obj => obj.m_NumBlocks * obj.m_BytesPerBlock);

                    var clrOperation = device.DebugEngine.ReadMemory(startAddress, length);
                    if (clrOperation.Success)
                    {
                        // copy to array
                        Array.Copy(clrOperation.Buffer, 0, binaryOutput, startAddress - flashStartAddress, length);
                    }
                    else
                    {
                        // check error code
                    }
                }

                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    Debug.WriteLine($">>> read flash memory completed <<<<");


                    //(DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.RebootDevice(RebootOptions.ClrOnly);

                    //Task.Delay(1000).Wait();

                    //(DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].GetDeviceInfo(true);

                }));
            }
            catch
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void OemInfoButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;


            await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => {

                var device = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex];


                var oemInfo = device.DebugEngine.GetMonitorOemInfo();

                if (oemInfo != null)
                {
                    Debug.WriteLine("");
                    Debug.WriteLine("");
                    Debug.WriteLine($"OEM info: {oemInfo.Info}");
                    Debug.WriteLine($"Platform: {oemInfo.PlatformName}");
                    Debug.WriteLine($"Target: {oemInfo.TargetName}");
                    Debug.WriteLine($"Platform Info: {oemInfo.PlatformInfo}");
                    Debug.WriteLine("");
                    Debug.WriteLine("");
                }
                else
                {
                    Debug.WriteLine("");
                    Debug.WriteLine("no OEM info available");
                    Debug.WriteLine("");
                }

            }));

            // enable button
            (sender as Button).IsEnabled = true;

        }
    }
}
