//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//

using System;
using System.Collections.Generic;
using System.Linq;
using Windows.UI.Popups;

namespace NanoFramework.ANT.Services.Dialog
{
    internal class DialogHelper
    {
        internal MessageDialog CreateDialog(string message, string title = null,
                                            List<Tuple<string, Action>> buttons = null, int? defaultIndex = null, int? cancelIndex = null,
                                            Action<int> afterHideCallback = null, Action<int> afterHideInternal = null)
        {
            var dialog = string.IsNullOrEmpty(title) ? new MessageDialog(message) : new MessageDialog(message, title);
            if (buttons != null && buttons.Any())
            {
                int i = 0;
                foreach (Tuple<string, Action> button in buttons)
                {
                    UICommand buttonCommand = new UICommand(button.Item1,
                                                            command =>
                                                            {
                                                                if (button.Item2 != null)
                                                                {
                                                                    button.Item2();
                                                                }
                                                                if (afterHideCallback != null)
                                                                {
                                                                    afterHideCallback((int)command.Id);
                                                                }
                                                                if (afterHideInternal != null)
                                                                {
                                                                    afterHideInternal((int)command.Id);
                                                                }
                                                            },
                                                            i);
                    dialog.Commands.Add(buttonCommand);
                    i++;
                }
                if (defaultIndex != null) dialog.DefaultCommandIndex = (uint)defaultIndex;
                if (cancelIndex != null) dialog.CancelCommandIndex = (uint)cancelIndex;
            }
            return dialog;
        }


        internal MessageDialog CreateDialog(string message, string title,
                                            List<string> buttons = null, int? defaultIndex = null, int? cancelIndex = null,
                                            Action afterHideCallback = null,
                                            Action<int> afterHideCallbackWithResponse = null, Action<int> afterHideInternal = null)
        {
            var dialog = string.IsNullOrEmpty(title) ? new MessageDialog(message) : new MessageDialog(message, title);
            if (buttons != null)
            {
                for (int i = 0; i < buttons.Count; i++)
                {
                    dialog.Commands.Add(new UICommand(buttons[i],
                                            command =>
                                            {

                                                if (afterHideCallback != null)
                                                {
                                                    afterHideCallback();
                                                }

                                                if (afterHideCallbackWithResponse != null)
                                                {
                                                    afterHideCallbackWithResponse((int)command.Id);
                                                }

                                                if (afterHideInternal != null)
                                                {
                                                    afterHideInternal((int)command.Id);
                                                }
                                            }, (int)i
                                        ));
                }

                if (defaultIndex != null) dialog.DefaultCommandIndex = (uint)defaultIndex;
                if (cancelIndex != null) dialog.CancelCommandIndex = (uint)cancelIndex;
            }
            return dialog;
        }

    }
}
