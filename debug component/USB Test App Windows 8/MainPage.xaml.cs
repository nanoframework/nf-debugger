using Microsoft.SPOT.Debugger;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace Test_App_Windows_8
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class MainPage : Page
    {
        public MainPage()
        {
            this.InitializeComponent();
        }

        private async void connectButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            bool connectResult = await App.NETMFUsbDebugClient.MFDevices[0].DebugEngine.ConnectAsync(3, 1000);

            var di = await App.NETMFUsbDebugClient.MFDevices[0].GetDeviceInfoAsync();

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void pingButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            var p = await App.NETMFUsbDebugClient.MFDevices[0].PingAsync();

            Debug.WriteLine("");
            Debug.WriteLine("");
            Debug.WriteLine("Ping response: " + p.ToString());
            Debug.WriteLine("");
            Debug.WriteLine("");

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void printMemoryMapButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            var mm = await App.NETMFUsbDebugClient.MFDevices[0].DebugEngine.GetMemoryMapAsync();

            Debug.WriteLine("");
            Debug.WriteLine("");
            Debug.WriteLine(mm.ToStringForOutput());
            Debug.WriteLine("");
            Debug.WriteLine("");

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private async void printFlashSectorMapButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            var fm = await App.NETMFUsbDebugClient.MFDevices[0].DebugEngine.GetFlashSectorMapAsync();

            Debug.WriteLine("");
            Debug.WriteLine("");
            Debug.WriteLine(fm.ToStringForOutput());
            Debug.WriteLine("");
            Debug.WriteLine("");

            // enable button
            (sender as Button).IsEnabled = true;
        }

        private void deployFileButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void disconnectButton_Click(object sender, RoutedEventArgs e)
        {
            // disable button
            (sender as Button).IsEnabled = false;

            App.NETMFUsbDebugClient.MFDevices[0].DebugEngine.Disconnect();

            // enable button
            (sender as Button).IsEnabled = true;
        }
    }
}
