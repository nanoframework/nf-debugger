//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using NanoFramework.ANT.ViewModels;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;

// The User Control item template is documented at http://go.microsoft.com/fwlink/?LinkId=234236

namespace NanoFramework.ANT.Controls
{
    public sealed partial class HeaderControl : UserControl
    {
        // strongly-typed view models enable x:bind
        public MainViewModel ViewModel => this.DataContext as MainViewModel;
        public HeaderControl()
        {
            this.InitializeComponent();
            AvailableTransportTypesListBox.DataContextChanged += AvailableTransportTypesListBox_DataContextChanged;
        }

        private void AvailableTransportTypesListBox_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
        {
            // this is needed because when property is set in vm, ui is not available yet and so selection is not visible
            AvailableTransportTypesListBox.DataContextChanged -= AvailableTransportTypesListBox_DataContextChanged;
            AvailableTransportTypesListBox.SelectedItem = ViewModel.SelectedTransportType;
        }

        private void AvailableTransportTypesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {            
            this.TransportTypeButton.Flyout.SetValue(Views.AttachProp.IsOpenProperty, false);
        }

        private void AvailableTransportTypesFlyout_Opened(object sender, object e)
        {
            this.TransportTypeButton.Flyout.SetValue(Views.AttachProp.IsOpenProperty, true);
        }


        private void AvailableDevicesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            this.AvailableDevicesFlyout.SetValue(Views.AttachProp.IsOpenProperty, false);
        }

        private void AvailableDevicesFlyout_Opened(object sender, object e)
        {
            this.AvailableDevicesFlyout.SetValue(Views.AttachProp.IsOpenProperty, true);
        }

        
    }
}
