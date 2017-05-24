//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using System;
using NanoFramework.ANT.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Navigation;
using System.Collections.ObjectModel;
using Microsoft.Practices.ServiceLocation;
using GalaSoft.MvvmLight.Messaging;
using System.Threading.Tasks;

namespace NanoFramework.ANT.Views
{
    public sealed partial class MainPage : Page
    {
        // strongly-typed view models enable x:bind
        public MainPageViewModel ViewModel => this.DataContext as MainPageViewModel;

        public MainPage()
        {
            InitializeComponent();
            NavigationCacheMode = Windows.UI.Xaml.Navigation.NavigationCacheMode.Enabled;
            this.Loaded += MainPage_Loaded;
            this.Unloaded += MainPage_Unloaded;

        }

        private void MainPage_Loaded(object sender, RoutedEventArgs e)
        {
            Messenger.Default.Register<NotificationMessage>(this, MainViewModel.WRITE_TO_OUTPUT_TOKEN, async (message) => await AddTextToOutput(message.Notification));
        }

        private void MainPage_Unloaded(object sender, RoutedEventArgs e)
        {
            Messenger.Default.Unregister(this);
        }

        private async Task AddTextToOutput(string text)
        {
            await Windows.ApplicationModel.Core.CoreApplication.MainView.CoreWindow.Dispatcher.RunAsync(Windows.UI.Core.CoreDispatcherPriority.Normal,
            () =>
            {
                this.Output.Text += (Environment.NewLine + text);
            });
        }

        private void clearOutputAppBarButton_Tapped(object sender, Windows.UI.Xaml.Input.TappedRoutedEventArgs e)
        {
            this.Output.Text = "";
        }
    }
}
