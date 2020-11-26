//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//
using nanoFramework.Tools.Debugger.Extensions;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Windows.Storage;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace Test_App_UWP
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private async void connectButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            bool connectResult = await App.NanoFrameworkUsbDebugClient.NanoFrameworkDevices[0].DebugEngine.ConnectAsync(3000, true);

            var di = App.NanoFrameworkUsbDebugClient.NanoFrameworkDevices[0].GetDeviceInfo();

            Debug.WriteLine("");
            Debug.WriteLine("");
            Debug.WriteLine(di.ToString());
            Debug.WriteLine("");
            Debug.WriteLine("");

            // enable button
            (sender as Button).IsEnabled = true;

            
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            var s = App.NanoFrameworkUsbDebugClient.NanoFrameworkDevices[0].DebugEngine.SendBuffer(new byte[] { (byte)'x', (byte)'x' }, TimeSpan.FromMilliseconds(1000), new CancellationToken());

            var r = App.NanoFrameworkUsbDebugClient.NanoFrameworkDevices[0].DebugEngine.ReadBuffer(10, TimeSpan.FromMilliseconds(1000), new CancellationToken());
        }

        private void pingButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;
            var p = App.NanoFrameworkUsbDebugClient.NanoFrameworkDevices[0].Ping();

            Debug.WriteLine("");
            Debug.WriteLine("");
            Debug.WriteLine("Ping response: " + p.ToString());
            Debug.WriteLine("");
            Debug.WriteLine("");

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private void printMemoryMapButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            var mm = App.NanoFrameworkUsbDebugClient.NanoFrameworkDevices[0].DebugEngine.GetMemoryMap();

            Debug.WriteLine("");
            Debug.WriteLine("");
            Debug.WriteLine(mm.ToStringForOutput());
            Debug.WriteLine("");
            Debug.WriteLine("");

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private void printFlashSectorMapButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            var fm = App.NanoFrameworkUsbDebugClient.NanoFrameworkDevices[0].DebugEngine.GetFlashSectorMap();
            //var fm = await App.NETMFUsbDebugClient.MFDevices[0].DebugEngine.GetAssembliesAsync();

            Debug.WriteLine("");
            Debug.WriteLine("");
            Debug.WriteLine(fm.ToStringForOutput());
            Debug.WriteLine("");
            Debug.WriteLine("");

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void deployFileButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            //// for this to work first need to copy the files nanoCLR.bin and nanoCLR.hex to Documents folder
            //string hexFile = Path.Combine(
            //                            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            //                            "nanoCLR.hex");

            //var reply = await App.NanoFrameworkUsbDebugClient.NanoFrameworkDevices[0].DeploySrecFileAsync(hexFile, CancellationToken.None, null);

            // now the BIN file
            string binFile = Path.Combine(
                                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                                        "nanoCLR.bin");

            ////////////////////////////////////////////////////////////////////////////////
            // MAKE sure to adjust this to the appropriate flash address of the CLR block //
            uint flashAddress = 0x08004000;
            ////////////////////////////////////////////////////////////////////////////////

            var reply1 = await App.NanoFrameworkUsbDebugClient.NanoFrameworkDevices[0].DeployBinaryFileAsync(binFile, flashAddress, CancellationToken.None, null);

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private void disconnectButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            App.NanoFrameworkUsbDebugClient.NanoFrameworkDevices[0].Disconnect();

            // enable button
            (sender as Button).IsEnabled = true;
        }
    }
}
