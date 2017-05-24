﻿//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Template10.Common;

namespace NanoFramework.ANT.Services.Dialog
{
    public class MyDialogService : IMyDialogService
    {
        private static bool isShowing = false;
        private DialogHelper _helper = new DialogHelper();

        /// <summary> Displays information to user with custom buttons </summary>
        /// <param name="message">Message to be displayed</param>
        /// <param name="title">Title of the dialog. This can be null</param>
        /// <param name="buttons">List of strings with button's text in order(left to right).This can be null (default close button is shown)</param>
        /// <param name="defaultIndex">index of default button (in the previous list). This can be null</param>
        /// <param name="cancelIndex">index of cancel button (in the previous list). This can be null</param>
        /// <param name="callBackAction">A callback that should be executed after the dialog box is closed by the user. This can be null
        /// <returns>index of button tapped or 99 if didn't show message (If another dialog is already in view)</returns>
        public async Task<int> ShowMessageAsync(string message, string title = "",
                                                List<string> buttons = null, int? defaultIndex = null, int? cancelIndex = null,
                                                Action<int> callBackAction = null)
        {
            // Only show one dialog at a time.
            if (!isShowing)
            {
                int result = 0;

                var dialog = _helper.CreateDialog(message, title, buttons, defaultIndex, cancelIndex, null, callBackAction, r => result = r);

                try
                {
                    isShowing = true;
                    await WindowWrapper.Current().Dispatcher.DispatchAsync(async () =>
                    {
                        await dialog.ShowAsync();
                    });
                    isShowing = false;
                    return result;
                }
                catch (Exception ex)
                {
                    if (!ex.Message.Contains("0x80070005")) //access denied - another dialog is visible
                    {
                        throw;
                    }
                    return 99;
                }
            }
            else
            {
                //write message to console for debug purposes
                Debug.WriteLine(string.Format("Couldn't show message: '{0}'", message));
                return 99;
            }

        }


        /// <summary> Displays information to user with custom buttons </summary>
        /// <param name="message">Message to be displayed</param>
        /// <param name="title">Title of the dialog. Optional</param>
        /// <param name="buttons">List of pairs (button text, action to perform when chosen) Optional (when not set, default close button is shown)</param>
        /// <param name="defaultIndex">index of default button (in the previous list). Optional</param>
        /// <param name="cancelIndex">index of cancel button (in the previous list). Optional</param>
        /// <param name="callBackAction">A callback that should be executed after the dialog box is closed by the user. Optional</param>
        /// <returns>index of button tapped or 99 if didn't show message (If another dialog is already in view)</returns>
        public async Task<int> ShowMessageWithActionsAsync(string message, string title = null,
                                                            List<Tuple<string, Action>> buttons = null, int? defaultIndex = null, int? cancelIndex = null,
                                                            Action<int> callBackAction = null)
        {
            // Only show one dialog at a time.
            if (!isShowing)
            {
                int result = 0;
                var dialog = _helper.CreateDialog(message, title, buttons, defaultIndex, cancelIndex, callBackAction, r => result = r);
                try
                {
                    isShowing = true;
                    await WindowWrapper.Current().Dispatcher.DispatchAsync(async () =>
                    {
                        await dialog.ShowAsync();
                    });

                    isShowing = false;
                    return result;
                }
                catch (Exception ex)
                {
                    if (!ex.Message.Contains("0x80070005")) //access denied - another dialog is visible
                    {
                        throw;
                    }
                    return 99;
                }
            }
            else
            {
                //write message to console for debug purposes
                Debug.WriteLine(string.Format("Couldn't show message: '{0}'", message));
                return 99;
            }

        }


        /// <summary> Displays information about an error in a message dialog </summary>
        /// <param name="error">exception (its message will de shown as the dialog message)</param>
        /// <param name="title">Dialog title. Optional</param>
        /// <param name="buttonText">Text for the button. Optional (when not set, default close button is shown)</param>
        /// <param name="afterHideCallback">A callback function to be executed after the user closes the dialog. Optional</param>
        /// <returns>Task to allow this to be awaited</returns>
        public async Task ShowErrorAsync(Exception error, string title = null, string buttonText = null, Action afterHideCallback = null)
        {
            // Only show one dialog at a time.
            if (!isShowing)
            {
                var dialog = _helper.CreateDialog(error.Message, title, string.IsNullOrEmpty(buttonText) ? null : new List<string> { buttonText }, 0, null, afterHideCallback);

                try
                {
                    isShowing = true;
                    await WindowWrapper.Current().Dispatcher.DispatchAsync(async () =>
                    {

                        await dialog.ShowAsync();
                    });
                    isShowing = false;
                }
                catch (Exception ex)
                {
                    if (!ex.Message.Contains("0x80070005")) //access denied - another dialog is visible
                    {
                        throw;
                    }
                }
            }
            else
            {
                //write error message to console for debug purposes
                Debug.WriteLine(string.Format("Couldn't show error message: '{0}'", error.Message));
            }
        }
    }
}
