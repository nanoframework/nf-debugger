//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using NanoFramework.ANT.Utilities;
using NanoFramework.ANT.ViewModels;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace NanoFramework.ANT.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class ConfigNetworkPage : Page
    {
        // strongly-typed view models enable x:bind
        public ConfigNetworkViewModel ViewModel => this.DataContext as ConfigNetworkViewModel;
        public ConfigNetworkPage()
        {
            this.InitializeComponent();

            // load radio type
            switch (ViewModel.Radio)
            {
                case RadioTypes.a:
                    radio80211aRadioButton.IsChecked = true;
                    break;
                case RadioTypes.g:
                    radio80211gRadioButton.IsChecked = true;
                    break;
                case RadioTypes.b:
                    radio80211bRadioButton.IsChecked = true;
                    break;
                case RadioTypes.n:
                    radio80211nRadioButton.IsChecked = true;
                    break;
            }

            // network key format
            switch (ViewModel.NetworkKey.Length)
            {
                case 16: // 8 bytes
                    networkKeyComboBox.SelectedIndex = 0; //64-bit
                    break;
                case 32: // 16 bytes
                    networkKeyComboBox.SelectedIndex = 1; //128-bit
                    break;
                case 64: // 32 bytes
                    networkKeyComboBox.SelectedIndex = 2; //256-bit
                    break;
                case 128: // 64 bytes
                    networkKeyComboBox.SelectedIndex = 3; //512-bit
                    break;
                case 256: // 128 bytes
                    networkKeyComboBox.SelectedIndex = 4; //1024-bit
                    break;
                case 512: // 256 bytes
                    networkKeyComboBox.SelectedIndex = 5; //2048-bit
                    break;
                default:
                    networkKeyComboBox.SelectedIndex = 0;
                    break;
            }

        }

        private void IPAdrressTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
        {
            int key = (int)e.Key;
            //  ALLOW           enter                          back                        delete          numbers 0-9          numeric keypad numbers 0-9       numeric keypad .              keypad .
            if (e.Key == VirtualKey.Enter || e.Key == VirtualKey.Back || e.Key == VirtualKey.Delete || (key >= 48 && key <= 57) || (key >= 96 && key <= 105) || e.Key == VirtualKey.Decimal || key == 190)
            {
                e.Handled = false;
            }
            else
            {
                e.Handled = true;
            }
        }

        private async Task IPAdrressTextBox_Paste(object sender, TextControlPasteEventArgs e)
        {
            TextBox tb = (sender as TextBox);

            var dataPackageView = Clipboard.GetContent();
            if (dataPackageView.Contains(StandardDataFormats.Text))
            {
                try
                {
                    // get text from clipboard
                    var text = await dataPackageView.GetTextAsync();
                    // use this regular expression to validate IP format
                    Regex nonNumericRegex = new Regex(@"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b");
                    if (!nonNumericRegex.IsMatch(text))
                    {
                        // no match, show error!
                        ShowInvalidAddressFormatErrorMsg(tb.Name);
                    }
                    else
                    {
                        // we have a match, hide error message in case it was shown
                        HideInvalidAddressFormatErrorMsg(tb.Name);
                    }
                }
                catch
                {

                }
            }
            // update button is enabled state
            UpdateIsEnabledButtonState();
        }

        private void IPAdrressTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            // use this regular expression to validate IP format
            Regex nonNumericRegex = new Regex(@"\b(?:(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\.){3}(?:25[0-5]|2[0-4][0-9]|[01]?[0-9][0-9]?)\b");
            if (!nonNumericRegex.IsMatch(tb.Text))
            {
                // no match, show error!
                ShowInvalidAddressFormatErrorMsg(tb.Name);
            }
            else
            {
                // we have a match, hide error message in case it was shown
                HideInvalidAddressFormatErrorMsg(tb.Name);
            }
            // update button is enabled state
            UpdateIsEnabledButtonState();
        }

        private async Task macAddressTextBox_Paste(object sender, TextControlPasteEventArgs e)
        {
            TextBox tb = (sender as TextBox);

            var dataPackageView = Clipboard.GetContent();
            if (dataPackageView.Contains(StandardDataFormats.Text))
            {
                try
                {
                    // get text from clipboard
                    var text = await dataPackageView.GetTextAsync();
                    // use this regular expression to validate IP format
                    Regex nonNumericRegex = new Regex(@"^([0-9a-fA-F][0-9a-fA-F]:){5}([0-9a-fA-F][0-9a-fA-F])$");
                    if (!nonNumericRegex.IsMatch(text))
                    {
                        // no match, show error!
                        ShowInvalidAddressFormatErrorMsg(tb.Name);
                    }
                    else
                    {
                        // we have a match, hide error message in case it was shown
                        HideInvalidAddressFormatErrorMsg(tb.Name);
                    }
                }
                catch
                {

                }
            }
            // update button is enabled state
            UpdateIsEnabledButtonState();
        }

        private void macAdrressTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            TextBox tb = sender as TextBox;
            // use this regular expression to validate MAC format
            Regex nonNumericRegex = new Regex(@"^([0-9a-fA-F][0-9a-fA-F]:){5}([0-9a-fA-F][0-9a-fA-F])$");
            if (!nonNumericRegex.IsMatch(tb.Text))
            {
                // no match, show error!
                ShowInvalidAddressFormatErrorMsg(tb.Name);
            }
            else
            {
                // we have a match, hide error message in case it was shown
                HideInvalidAddressFormatErrorMsg(tb.Name);
            }
            // update button is enabled state
            UpdateIsEnabledButtonState();
        }

        /// <summary>
        /// Show invalid address format error message
        /// </summary>
        /// <param name="name">textblock name to show</param>
        private void ShowInvalidAddressFormatErrorMsg(string name)
        {
            switch (name)
            {
                case "staticIPAdrressTextBox":
                    staticIPAdrressTextBlock.Text = Res.GetString("invalidIPAddressMsg");
                    break;
                case "subnetMaskTextBox":
                    subnetMaskTextBlock.Text = Res.GetString("invalidIPAddressMsg");
                    break;
                case "defaultGatewayTextBox":
                    defaultGatewayTextBlock.Text = Res.GetString("invalidIPAddressMsg");
                    break;
                case "macAdrressTextBox":
                    macAdrressTextBlock.Text = Res.GetString("invalidMACAddressMsg");
                    break;
                case "dnsPrimaryAdrressTextBox":
                    dnsPrimaryAdrressTextBlock.Text = Res.GetString("invalidIPAddressMsg");
                    break;
                case "dnsSecondaryAdrressTextBox":
                    dnsSecondaryAdrressTextBlock.Text = Res.GetString("invalidIPAddressMsg");
                    break;
            }
        }

        /// <summary>
        /// hide invalid address format error message
        /// </summary>
        /// <param name="name">textblock name to hide</param>
        private void HideInvalidAddressFormatErrorMsg(string name)
        {
            switch (name)
            {
                case "staticIPAdrressTextBox":
                    staticIPAdrressTextBlock.Text = string.Empty;
                    break;
                case "subnetMaskTextBox":
                    subnetMaskTextBlock.Text = string.Empty;
                    break;
                case "defaultGatewayTextBox":
                    defaultGatewayTextBlock.Text = string.Empty;
                    break;
                case "macAdrressTextBox":
                    macAdrressTextBlock.Text = string.Empty;
                    break;
                case "dnsPrimaryAdrressTextBox":
                    dnsPrimaryAdrressTextBlock.Text = string.Empty;
                    break;
                case "dnsSecondaryAdrressTextBox":
                    dnsSecondaryAdrressTextBlock.Text = string.Empty;
                    break;
            }
        }

        private void networkKeyComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            switch (networkKeyComboBox.SelectedIndex)
            {
                case 0: // 64-bit
                    networkKeyTextBox.MaxLength = 16; //8 bytes
                    break;
                case 1: // 128-bit
                    networkKeyTextBox.MaxLength = 32; //16 bytes
                    break;
                case 2: // 256-bit
                    networkKeyTextBox.MaxLength = 64; //32 bytes
                    break;
                case 3: // 512-bit
                    networkKeyTextBox.MaxLength = 128; //64 bytes
                    break;
                case 4: // 1024-bit
                    networkKeyTextBox.MaxLength = 256; //128 bytes
                    break;
                case 5: // 2048-bit
                    networkKeyTextBox.MaxLength = 512; //256 bytes
                    break;
            }
            // short key if actual length is bigger then max size allowed
            if (networkKeyTextBox.Text.Length > networkKeyTextBox.MaxLength)
            {
                networkKeyTextBox.Text = networkKeyTextBox.Text.Substring(0, networkKeyTextBox.MaxLength);
            }
            // update button is enabled state
            UpdateIsEnabledButtonState();
        }

        /// <summary>
        /// Enable update button if no warning message is active/visible, otherwise disable button
        /// </summary>
        private void UpdateIsEnabledButtonState()
        {
            // enable update button if no warning is active
            if (staticIPAdrressTextBlock.Text.Length == 0 &&
                subnetMaskTextBlock.Text.Length == 0 &&
                defaultGatewayTextBlock.Text.Length == 0 &&
                macAdrressTextBlock.Text.Length == 0 &&
                dnsPrimaryAdrressTextBlock.Text.Length == 0 &&
                dnsSecondaryAdrressTextBlock.Text.Length == 0)
            {
                ViewModel.UpdateButtonEnabled = true;
            }
            else
            {
                ViewModel.UpdateButtonEnabled = false;
            }
        }

        private void radio80211RadioButton_Checked(object sender, RoutedEventArgs e)
        {
            RadioButton cb = sender as RadioButton;
            // set radio type
            switch (cb.Name)
            {
                case "radio80211aRadioButton":
                    ViewModel.Radio = RadioTypes.a;
                    break;
                case "radio80211gRadioButton":
                    ViewModel.Radio = RadioTypes.g;
                    break;
                case "radio80211bRadioButton":
                    ViewModel.Radio = RadioTypes.b;
                    break;
                case "radio80211nRadioButton":
                    ViewModel.Radio = RadioTypes.n;
                    break;
            }
        }
    }
}
