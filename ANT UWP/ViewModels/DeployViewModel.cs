//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using GalaSoft.MvvmLight.Messaging;
using Microsoft.Practices.ServiceLocation;
using NanoFramework.ANT.Models;
using NanoFramework.ANT.Services.BusyService;
using NanoFramework.ANT.Services.Dialog;
using NanoFramework.ANT.Services.StorageService;
using NanoFramework.ANT.Utilities;
using NanoFramework.ANT.Views.Config;
using NanoFramework.Tools.Debugger;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Template10.Common;
using Template10.Services.NavigationService;
using Windows.Storage;
using Windows.UI.Xaml.Navigation;

namespace NanoFramework.ANT.ViewModels
{
    public class DeployViewModel : MyViewModelBase
    {
        //private instance of Main to get general stuff
        private MainViewModel MainVM { get { return ServiceLocator.Current.GetInstance<MainViewModel>(); } }

        public DeployViewModel(IMyDialogService dlg, IBusyService busy, IStorageInterfaceService _storageInterfaceService)
        {
            this.DialogSrv = dlg;
            this.BusySrv = busy;
            StorageInterface = _storageInterfaceService;
        }

        #region Navigation
        public override async Task OnNavigatedToAsync(object parameter, NavigationMode mode, IDictionary<string, object> suspensionState)
        {

            if (suspensionState.Any())
            {
                //Value = suspensionState[nameof(Value)]?.ToString();
            }
            MessengerInstance.Register<NotificationMessage>(this, MainViewModel.SELECTED_NULL_TOKEN, (message) => SelectedIsNullHandler());
            await Task.CompletedTask;

            MainVM.PageHeader = Res.GetString("DP_PageHeader");

            if (FilesList != null)
                FilesListLoaded?.Invoke(this, EventArgs.Empty);
        }

        public override async Task OnNavigatedFromAsync(IDictionary<string, object> suspensionState, bool suspending)
        {
            if (suspending)
            {
                //suspensionState[nameof(Value)] = Value;
            }
            MessengerInstance.Unregister(this);
            await Task.CompletedTask;
            // clear event handler
            FilesListLoaded = null;
        }

        public override async Task OnNavigatingFromAsync(NavigatingEventArgs args)
        {
            args.Cancel = false;
            await Task.CompletedTask;
        }

        #endregion
        private void SelectedIsNullHandler()
        {
            this.NavigationService.Navigate(Pages.MainPage);
        }


        public ObservableCollection<DeployFile> FilesList { get; set; }

        public bool AnyFileSelected { get; set; }

        public CancellationTokenSource CurrentDeploymentTokenSource { get; set; }

        public event EventHandler FilesListLoaded;

        /// <summary>
        /// Opens file picker and populates files list
        /// </summary>
        public async Task OpenDeployFiles()
        {
            // check if user has a worker folder
            if (StorageInterface.IsDeployFolderAvailable)
            {
                // get worker folder
                StorageFolder folder = await StorageInterface.GetDeployFolder();
                // get files from worker folder
                IReadOnlyList<StorageFile> files = await StorageInterface.GetDeployFiles();
                // handle each file
                if (files?.Count > 0)
                {
                    // new list
                    FilesList = new ObservableCollection<DeployFile>();

                    // get each file and add it to collection
                    foreach (StorageFile file in files)
                    {
                        // check for allowed extensions
                        if (Path.GetExtension(file.Path).ToLower() == ".sig")
                        {
                            // this type of file will be use latter, not now
                            continue;
                        }
                        else if (Path.GetExtension(file.Path).ToLower() != ".nmf" &&
                            Path.GetExtension(file.Path).ToLower() != ".hex")
                        {
                            // file as different or no extension
                            // allowed files without extension are ER_FLASH, ER_RAM, ER_CONFIG, ER_DAT, ER_ResetVector
                            if (file.DisplayName != "ER_FLASH" && file.DisplayName != "ER_RAM" && file.DisplayName != "ER_CONFIG" &&
                                file.DisplayName != "ER_DAT" && file.DisplayName != "ER_ResetVector")
                            {
                                // file not allowed
                                continue;
                            }
                        }
                        // add new files
                        FilesList.Add(new DeployFile(file));
                    }
                    // any file
                    if (FilesList.Count == 0)
                    {
                        var dummy = await DialogSrv.ShowMessageAsync(Res.GetString("DP_NoValidFiles"));
                        return;
                    }
                    FilesListLoaded?.Invoke(this, EventArgs.Empty);
                }
                else
                {
                    var dummy = await DialogSrv.ShowMessageAsync(Res.GetString("DP_NoFiles"));
                }
            }
            else
            {
                // user haven't pick a folder yet, notify him
                List<Tuple<string, Action>> buttons = new List<Tuple<string, Action>>
                {
                    new Tuple<string, Action>(Res.GetString("DP_GoToSettings"), () => NavigationService.Navigate(Pages.SettingsPage, 0)),
                    new Tuple<string, Action>(Res.GetString("DP_Close"), null)
                };
                await DialogSrv.ShowMessageWithActionsAsync(Res.GetString("DP_NoWorkerFolder"), "", buttons, 0, 1);
            }
        }

        public async Task DeployFile()
        {
            bool success = false;

            // get only selected files
            IEnumerable<DeployFile> deployList = FilesList.ToArray().Where(s => s.Selected == true);

            // sanity check
            if (deployList.Count() <= 0)
                return;

            List<StorageFile> sigfiles = new List<StorageFile>(deployList.Count());

            // show busy
            BusySrv.ShowBusy();

            try
            {
                foreach (DeployFile file in deployList)
                {
                    // sanity checks
                    if (file.DFile.Path.Trim().Length == 0)
                        continue;
                    if (!file.DFile.IsAvailable)
                    {
                        BusySrv.HideBusy();
                        var dummy = await DialogSrv.ShowMessageAsync(String.Format(Res.GetString("DP_CantOpenFile"), file.DFile.DisplayName));
                        return;
                    }

                    // add to sigFiles list, if exists
                    var sigFile = await GetSignatureFileName(file.DFile);
                    if (sigFile == null)
                    {
                        var dummy = await DialogSrv.ShowMessageAsync(String.Format(Res.GetString("DP_CanOpenSigFile"), file.DFile.DisplayName));
                        return;
                    }
                    sigfiles.Add(sigFile);
                }

                // the code to deploy file goes here...

                // fazer ping
                bool fMicroBooter = (await MainVM.SelectedDevice.PingAsync() == PingConnectionType.NanoBooter);
                if (fMicroBooter)
                {
                    await MainVM.SelectedDevice.DebugEngine.PauseExecutionAsync();
                }

                List<uint> executionPoints = new List<uint>();
                int cnt = 0;
                foreach (DeployFile file in deployList)
                {
                    WindowWrapper.Current().Dispatcher.Dispatch(() =>
                    {
                        CurrentDeploymentTokenSource = new CancellationTokenSource();
                    });
                    CancellationToken cancellationToken = CurrentDeploymentTokenSource.Token;

                    if (Path.GetExtension(file.DFile.Path).ToLower() == ".nmf")
                    {

                        if (!await MainVM.SelectedDevice.DeployUpdateAsync(file.DFile, cancellationToken, 
                            new Progress<ProgressReport>((value) =>
                            {
                                // update busy message according to deployment progress
                                BusySrv.ChangeBusyText(value.Status);
                            })
                        ))
                        {
                            // fail
                            var dummy = await DialogSrv.ShowMessageAsync(String.Format(Res.GetString("DP_CantDeploy"), file.DFile.DisplayName));
                            return;
                        }
                    }
                    else
                    {
                        var tpl = await MainVM.SelectedDevice.DeployAsync(file.DFile, sigfiles[cnt++], cancellationToken, 
                            new Progress<ProgressReport>((value) => 
                            {
                                // update busy message according to deployment progress
                                BusySrv.ChangeBusyText(value.Status);
                            })
                        );

                        if (!tpl.Item2)
                        {
                            // fail
                            var dummy = await DialogSrv.ShowMessageAsync(String.Format(Res.GetString("DP_CantDeploy"), file.DFile.DisplayName));
                            return;
                        }
                        if (tpl.Item1 != 0)
                        {
                            executionPoints.Add(tpl.Item1);
                        }
                    }
                }

                executionPoints.Add(0);

                if (!fMicroBooter)
                {
                    for (int i = 0; i < 10; i++)
                    {
                        if (await MainVM.SelectedDevice.DebugEngine.ConnectAsync(1, 500, true))
                        {
                            break;
                        }
                    }
                }

                // update busy message according to deployment progress
                BusySrv.ChangeBusyText(Res.GetString("DP_ExecutingApp"));

                foreach (uint addr in executionPoints)
                {
                    WindowWrapper.Current().Dispatcher.Dispatch(() =>
                    {
                        CurrentDeploymentTokenSource = new CancellationTokenSource();
                    });
                    CancellationToken cancellationToken = CurrentDeploymentTokenSource.Token;
                    if (await MainVM.SelectedDevice.ExecuteAync(addr, cancellationToken))
                    {
                        break;
                    }
                }


                success = true;
            }
            catch { /* TBD */ }
            finally
            {
                // resume execution
                if (MainVM.SelectedDevice.DebugEngine != null)
                {
                    try
                    {
                        if (MainVM.SelectedDevice.DebugEngine.IsConnected && MainVM.SelectedDevice.DebugEngine.ConnectionSource == NanoFramework.Tools.Debugger.WireProtocol.ConnectionSource.NanoCLR)
                        {
                            await MainVM.SelectedDevice.DebugEngine.ResumeExecutionAsync();
                        }
                    }
                    catch
                    {
                    }
                }

                // end busy
                BusySrv.HideBusy();

                // show result to user
                if (success)
                {
                    await DialogSrv.ShowMessageAsync(deployList.Count() > 1 ? Res.GetString("DP_FilesDeployed") : Res.GetString("DP_FileDeployed"));
                }
                else
                {
                    await DialogSrv.ShowMessageAsync(deployList.Count() > 1 ? Res.GetString("DP_FailToDeployFiles") : Res.GetString("DP_FailToDeployFile"));
                }
            }
        }

        /// <summary>
        /// Gets the corresponding sig file type
        /// </summary>
        /// <param name="file">file to be deployed</param>
        /// <returns>The corresponding sig file, null if not found</returns>
        private async Task<StorageFile> GetSignatureFileName(StorageFile file)
        {
            // get file folder
            var folder = await file.GetParentAsync();
            
            // get .sig file version
            var sigFile = await folder.TryGetItemAsync(Path.GetFileNameWithoutExtension(file.Name) + ".sig") as StorageFile;
            if (sigFile != null)
            {
                // file found, return it
                return sigFile;
            }
            // the .sig file version doesn't exists, return null
            return null;
        }
    }
}
