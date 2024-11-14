//
// Copyright (c) .NET Foundation and Contributors
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
using System.Linq;
using System.Net;
using System.Security.Cryptography.X509Certificates;
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
        // number of retries when performing a deploy operation
        private const int _numberOfRetries = 5;
        // timeout when performing a deploy operation
        private const int _timeoutMiliseconds = 1000;

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

        private void ConnectDeviceButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            var device = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex];

            if(device.DebugEngine == null)
            {
                device.CreateDebugEngine(NanoSerialDevice.SafeDefaultTimeout);
            }

            bool connectResult = device.DebugEngine.Connect(5000, true, true);

            if(connectResult)
            {
                device.DebugEngine.OnProcessExit += DebugEngine_OnProcessExit;

                var di = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].GetDeviceInfo();

                Debug.WriteLine("");
                Debug.WriteLine("");
                Debug.WriteLine(di.ToString());
                Debug.WriteLine("");
                Debug.WriteLine("");

                Debug.WriteLine("Device capabilities:");

                Debug.Write("IFU capable: ");
                if (device.IsIFUCapable)
                {
                    Debug.WriteLine("YES");
                }
                else
                {
                    Debug.WriteLine("NO");
                }

                Debug.Write("Has proprietary bootloader: ");
                if (device.HasProprietaryBooter)
                {
                    Debug.WriteLine("YES");
                }
                else
                {
                    Debug.WriteLine("NO");
                }
            }

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
                    if ((DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.IsDeviceInInitializeState())
                    {
                        Debug.WriteLine($">>> Device is in initialized state <<<<");
                    }
                    else if ((DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.IsDeviceInProgramRunningState())
                    {
                        Debug.WriteLine($">>> Device is running a program <<<<");
                    }
                    else if ((DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.IsDeviceInExitedState())
                    {
                        Debug.WriteLine($">>> Device it's idle after exiting from a program execution <<<<");
                    }
                    else if ((DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.IsDeviceStoppedOnTypeResolutionFailed())
                    {
                        Debug.WriteLine($">>> Device can't start execution because type resolution has failed <<<<");
                    }
                    else
                    {
                        Debug.WriteLine($">>> Couldn't determine device state <<<<");
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

            int retryCount = 0;

            List<byte[]> assemblies = new List<byte[]>();
            assemblies.Add(File.ReadAllBytes("NFApp34.bin"));

            var totalSize = assemblies[0].Length;

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

                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(async () =>
                {
                    var device = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex];
                    var debugEngine = device.DebugEngine;

                    ///////////////////////////////////////////////////////////////
                    // process replicated from VS deploy provider
                    ///////////////////////////////////////////////////////////////

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

        private async void RebootToBootloaderButton_Click(object sender, RoutedEventArgs e)
        {  
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                await Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    // enable button
                    (sender as Button).IsEnabled = true;

                    (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.RebootDevice(RebootOptions.EnterProprietaryBooter);

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

        private void EraseDeploymentButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].Erase(EraseOptions.Deployment);

                Debug.WriteLine("");
                Debug.WriteLine("");
                Debug.WriteLine($"Erased deployment area SUCCESFULL.");
                Debug.WriteLine("");
                Debug.WriteLine("");
            }
            catch
            { 

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private void IsInitStateButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                var isInitState = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.IsDeviceInInitializeState();

                Debug.WriteLine("");
                Debug.WriteLine("");
                Debug.WriteLine($"device is in init state: {isInitState}");
                Debug.WriteLine("");
                Debug.WriteLine("");
            }
            catch
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private void GetDeviceConfigButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                // Create cancellation token source
                CancellationTokenSource cts = new CancellationTokenSource();

                var device = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex];

                // get device info
                var deviceConfig = device.DebugEngine.GetDeviceConfiguration(cts.Token);

                if (deviceConfig.NetworkConfigurations.Count > 0)
                {
                    Debug.WriteLine(deviceConfig.NetworkConfigurations[0].ToStringForOutput());
                }

                if (deviceConfig.Wireless80211Configurations.Count > 0)
                {
                    Debug.WriteLine(deviceConfig.Wireless80211Configurations[0].ToStringForOutput());
                }

                if (deviceConfig.WirelessAPConfigurations.Count > 0)
                {
                    Debug.WriteLine(deviceConfig.WirelessAPConfigurations[0].ToStringForOutput());
                }

                if (deviceConfig.X509Certificates.Count > 0)
                {
                    X509Certificate2 cert = new X509Certificate2(deviceConfig.X509Certificates[0].Certificate);
                    Debug.WriteLine(cert.ToString());
                }

                if (deviceConfig.X509DeviceCertificates.Count > 0)
                {
                    X509Certificate2 deviceCert = new X509Certificate2(deviceConfig.X509Certificates[0].Certificate);
                    Debug.WriteLine(deviceCert.ToString());
                }

                //Debug.WriteLine(string.Empty);
                //Debug.WriteLine(string.Empty);
                //Debug.WriteLine("--------------------------------");
                //Debug.WriteLine("::        Memory Map          ::");
                //Debug.WriteLine("--------------------------------");


            }
            catch(Exception ex)
            {

            }

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
        private void SetDeviceConfigButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                // Create cancellation token source
                CancellationTokenSource cts = new CancellationTokenSource();

                var device = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex];

                //DeviceConfiguration deviceConfig;

                //// get device info, if needed
                //if (device.DebugEngine.ConfigBlockRequiresErase)
                //{
                //    deviceConfig = device.DebugEngine.GetDeviceConfiguration(cts.Token);
                //}

                //// update new network configuration
                //DeviceConfiguration.NetworkConfigurationProperties newDeviceNetworkConfiguration = new DeviceConfiguration.NetworkConfigurationProperties
                //{
                //    MacAddress = new byte[] { 0, 0x80, 0xe1, 0x01, 0x35, 0x56 },
                //    InterfaceType = NetworkInterfaceType.Ethernet,
                //    StartupAddressMode = AddressMode.DHCP,

                //    IPv4DNSAddress1 = IPAddress.Parse("192.168.1.254"),
                //};

                //// write device configuration to device
                //var returnValue = device.DebugEngine.UpdateDeviceConfiguration(newDeviceNetworkConfiguration, 0);

                // add new wireless 802.11 configuration
                DeviceConfiguration.Wireless80211ConfigurationProperties newWireless80211Configuration = new DeviceConfiguration.Wireless80211ConfigurationProperties()
                {
                    Id = 0,
                    Ssid = "Nice_Ssid",
                    Password = "1234",
                    Authentication = AuthenticationType.WPA2,
                    Encryption = EncryptionType.WPA2,
                    Wireless80211Options = Wireless80211_ConfigurationOptions.AutoConnect
                };

                // write wireless configuration to device
                var returnValue = device.DebugEngine.UpdateDeviceConfiguration(newWireless80211Configuration, 0);

                // build a CA certificate bundle
                DeviceConfiguration.X509CaRootBundleProperties newX509CertificateBundle = new DeviceConfiguration.X509CaRootBundleProperties();

                //// add CA root certificates

                ///////////////////////////////////////////////////////////
                //// BECAUSE WE ARE PARSING FROM A BASE64 encoded format //
                //// NEED TO ADD A TERMINATOR TO THE STRING              //
                ///////////////////////////////////////////////////////////

                ////string caRootBundle = baltimoreCACertificate + letsEncryptCACertificate + "\0";

                ////byte[] certificateRaw = Encoding.UTF8.GetBytes(caRootBundle);

                //using (FileStream binFile = new FileStream(@"C:\Users\JoséSimões\Downloads\DigiCertGlobalRootCA.crt", FileMode.Open))
                //{
                //    newX509CertificateBundle.Certificate = new byte[binFile.Length];
                //    binFile.Read(newX509CertificateBundle.Certificate, 0, (int)binFile.Length);
                //    newX509CertificateBundle.CertificateSize = (uint)binFile.Length;
                //}

                //newX509CertificateBundle.Certificate = certificateRaw;

                // write CA certificate to device
                //var returnValue = device.DebugEngine.UpdateDeviceConfiguration(newX509CertificateBundle, 0);


                //// build a device certificate 
                //DeviceConfiguration.X509DeviceCertificatesProperties newX509DeviceCertificate = new DeviceConfiguration.X509DeviceCertificatesProperties();

                //using (FileStream binFile = new FileStream(@"C:\Users\JoséSimões\Downloads\OilLevelCert.pfx", FileMode.Open))
                //{
                //    newX509DeviceCertificate.Certificate = new byte[binFile.Length];
                //    binFile.Read(newX509DeviceCertificate.Certificate, 0, (int)binFile.Length);
                //    newX509DeviceCertificate.CertificateSize = (uint)binFile.Length;
                //}

                //// write certificate to device
                //var returnValue = device.DebugEngine.UpdateDeviceConfiguration(newX509DeviceCertificate, 0);



                Debug.WriteLine("");
                Debug.WriteLine("");
                Debug.WriteLine($"device config update result: {returnValue}");
                Debug.WriteLine("");
                Debug.WriteLine("");

            }
            catch (Exception ex)
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private void ReScanDevices_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            Task.Run(delegate
            {
                Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    (DataContext as MainViewModel).SerialDebugService.SerialDebugClient.ReScanDevices();

                    // need to wait for devices enumeration to complete
                    while (!(DataContext as MainViewModel).SerialDebugService.SerialDebugClient.IsDevicesEnumerationComplete)
                    {
                        Thread.Sleep(100);
                    }

                    // enable button
                    (sender as Button).IsEnabled = true;
                }));

            });
        }

        private void ReadTestButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                var device = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex];

                // get memory map
                var memoryMap = device.DebugEngine.GetMemoryMap();

                // get flash map
                var flashSectorMap = device.DebugEngine.GetFlashSectorMap();

                // setup array for binary output
                byte[] binaryOutput = new byte[memoryMap.First(m => (m.m_flags & Commands.Monitor_MemoryMap.c_FLASH) == Commands.Monitor_MemoryMap.c_FLASH).m_length];
                var flashStartAddress = memoryMap.First(m => (m.m_flags & Commands.Monitor_MemoryMap.c_FLASH) == Commands.Monitor_MemoryMap.c_FLASH).m_address;

                // bootloader
                if (flashSectorMap.Exists(item => (item.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_BOOTSTRAP))
                {
                    var startAddress = flashSectorMap.First(item => (item.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_BOOTSTRAP).StartAddress;
                    var length = (uint)flashSectorMap.Where(item => (item.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_BOOTSTRAP).Sum(obj => obj.NumBlocks * obj.BytesPerBlock);

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
                if (flashSectorMap.Exists(item => (item.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE))
                {
                    var startAddress = flashSectorMap.First(item => (item.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE).StartAddress;
                    var length = (uint)flashSectorMap.Where(item => (item.Flags & Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_MASK) == Commands.Monitor_FlashSectorMap.c_MEMORY_USAGE_CODE).Sum(obj => obj.NumBlocks * obj.BytesPerBlock);

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
            }
            catch
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private void TargetInfoButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            var device = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex];


            var targetInfo = device.DebugEngine.GetMonitorTargetInfo();

            if (targetInfo != null)
            {
                Debug.WriteLine("");
                Debug.WriteLine("");
                Debug.WriteLine($"{targetInfo.ToString()}");
                Debug.WriteLine("");
                Debug.WriteLine("");
            }
            else
            {
                Debug.WriteLine("");
                Debug.WriteLine("no OEM info available");
                Debug.WriteLine("");
            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private void RebootToNanoBooterButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            try
            {
                (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.RebootDevice(RebootOptions.EnterNanoBooter);

                Debug.WriteLine("");
                Debug.WriteLine("");
                Debug.WriteLine($"Reboot & launch nanoBooter");
                Debug.WriteLine("");
                Debug.WriteLine("");
            }
            catch
            {

            }

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private void DeployFileTestButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            // for this to work first need to copy the files nanoCLR.bin and nanoCLR.s19 to Documents folder
           
            //string hexFile = Path.Combine(
            //                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            //                            "nanoCLR.s19");

            //var reply = await (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DeploySrecFileAsync(hexFile, CancellationToken.None, null);


            // now the BIN file
            string binFile = Path.Combine(
                                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                        "nanoCLR.bin");

            ////////////////////////////////////////////////////////////////////////////////
            // MAKE sure to adjust this to the appropriate flash address of the CLR block //
            uint flashAddress = 0x08004000;
            ////////////////////////////////////////////////////////////////////////////////

            var reply1 = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DeployBinaryFile(binFile, flashAddress, null);

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private void UploadFileInternalStorage_Click(object sender, RoutedEventArgs e)
        {
            string fileContent = "1. This is a test file to upload in internal storage. A long message to test more than just a line.\r\n" +
                "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. " +
                "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. " +
                "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. " +
                "2. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.\r\n" +
                "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. " +
                "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. " +
                "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. " +
                "3. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.\r\n" +
                "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. " +
                "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. " +
                "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. " +
                "4. Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.\r\n";
            //string fileContent = "simple test file";
            string fileName = "I:\\upload.txt";

            // disable button
            (sender as Button).IsEnabled = false;

            var reply1 = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.AddStorageFile(fileName, Encoding.UTF8.GetBytes(fileContent));
            Debug.WriteLine($"File upload internal success: {reply1}");

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private void RemoveFileInternalStorage_Click(object sender, RoutedEventArgs e)
        {
            string fileName = "I:\\upload.txt";
            // disable button
            (sender as Button).IsEnabled = false;

            var reply1 = (DataContext as MainViewModel).AvailableDevices[DeviceGrid.SelectedIndex].DebugEngine.DeleteStorageFile(fileName);
            Debug.WriteLine($"File upload internal success: {reply1}");

            // enable button
            (sender as Button).IsEnabled = true;
        }
    }
}
