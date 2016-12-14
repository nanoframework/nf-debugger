using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace MFDeploy.Services.Dialog
{
    public interface IMyDialogService
    {
        /// <summary> Displays information to user with custom buttons </summary>
        /// <param name="message">Message to be displayed</param>
        /// <param name="title">Title of the dialog. This can be null</param>
        /// <param name="buttons">List of strings with button's text in order(left to right).This can be null (default close button is shown)</param>
        /// <param name="defaultIndex">index of default button (in the previous list). This can be null</param>
        /// <param name="cancelIndex">index of cancel button (in the previous list). This can be null</param>
        /// <param name="callBackAction">A callback that should be executed after the dialog box is closed by the user. This can be null
        /// <returns>index of button tapped or 99 if didn't show message (If another dialog is already in view)</returns>
        Task<int> ShowMessageAsync(string message, string title = "",
                                    List<string> buttons = null, int? defaultIndex = null, int? cancelIndex = null,
                                    Action<int> callBackAction = null);


        /// <summary> Displays information to user with custom buttons </summary>
        /// <param name="message">Message to be displayed</param>
        /// <param name="title">Title of the dialog. Optional</param>
        /// <param name="buttons">List of pairs (button text, action to perform when chosen) Optional (when not set, default close button is shown)</param>
        /// <param name="defaultIndex">index of default button (in the previous list). Optional</param>
        /// <param name="cancelIndex">index of cancel button (in the previous list). Optional</param>
        /// <param name="callBackAction">A callback that should be executed after the dialog box is closed by the user. Optional</param>
        /// <returns>index of button tapped or 99 if didn't show message (If another dialog is already in view)</returns>
        Task<int> ShowMessageWithActionsAsync(string message, string title = null,
                                               List<Tuple<string, Action>> buttons = null, int? defaultIndex = null, int? cancelIndex = null,
                                               Action<int> callBackAction = null);


        /// <summary> Displays information about an error in a message dialog </summary>
        /// <param name="error">exception (its message will de shown as the dialog message)</param>
        /// <param name="title">Dialog title. Optional</param>
        /// <param name="buttonText">Text for the button. Optional (when not set, default close button is shown)</param>
        /// <param name="afterHideCallback">A callback function to be executed after the user closes the dialog. Optional</param>
        /// <returns>Task to allow this to be awaited</returns>  
        Task ShowErrorAsync(Exception error, string title = null, string buttonText = null, Action afterHideCallback = null);
    }
}
