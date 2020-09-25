//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//
using nanoFramework.Tools.Debugger.Extensions;
using System;
using System.Diagnostics;
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

        private async void button1_Click(object sender, RoutedEventArgs e)
        {
            var s = await App.NanoFrameworkUsbDebugClient.NanoFrameworkDevices[0].DebugEngine.SendBufferAsync(new byte[] { (byte)'x', (byte)'x' }, TimeSpan.FromMilliseconds(1000), new CancellationToken());

            var r = await App.NanoFrameworkUsbDebugClient.NanoFrameworkDevices[0].DebugEngine.ReadBufferAsync(10, TimeSpan.FromMilliseconds(1000), new CancellationToken());
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

            // for this to work first need to copy the files ER_CONFIG and ER_CONFIG.sig to Documents folder
            StorageFolder storageFolder = await KnownFolders.GetFolderForUserAsync(null /* current user */, KnownFolderId.DocumentsLibrary);

            StorageFile srecFile = await storageFolder.TryGetItemAsync("ER_CONFIG") as StorageFile;

            var reply = await App.NanoFrameworkUsbDebugClient.NanoFrameworkDevices[0].DeployAsync(srecFile, CancellationToken.None, null);


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
