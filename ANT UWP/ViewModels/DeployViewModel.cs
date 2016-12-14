using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MFDeploy.Services.BusyService;
using MFDeploy.Services.Dialog;
using MFDeploy.Utilities;
using Microsoft.Practices.ServiceLocation;
using Template10.Services.NavigationService;
using Windows.UI.Xaml.Navigation;
using Windows.Storage.Pickers;
using Windows.Storage;
using MFDeploy.Models;
using System.Collections.ObjectModel;
using Template10.Controls;

namespace MFDeploy.ViewModels
{
    public class DeployViewModel : MyViewModelBase
    {
        //private instance of Main to get general stuff
        private MainViewModel MainVM { get { return ServiceLocator.Current.GetInstance<MainViewModel>(); } }

        public DeployViewModel(IMyDialogService dlg, IBusyService busy)
        {
            this.DialogSrv = dlg;
            this.BusySrv = busy;
        }

        #region Navigation
        public override async Task OnNavigatedToAsync(object parameter, NavigationMode mode, IDictionary<string, object> suspensionState)
        {

            if (suspensionState.Any())
            {
                //Value = suspensionState[nameof(Value)]?.ToString();
            }
            await Task.CompletedTask;

            MainVM.PageHeader = Res.GetString("DP_PageHeader");
        }

        public override async Task OnNavigatedFromAsync(IDictionary<string, object> suspensionState, bool suspending)
        {
            if (suspending)
            {
                //suspensionState[nameof(Value)] = Value;
            }
            await Task.CompletedTask;
        }

        public override async Task OnNavigatingFromAsync(NavigatingEventArgs args)
        {
            args.Cancel = false;
            await Task.CompletedTask;
        }

        #endregion

        public ObservableCollection<DeployFile> FilesList { get; set; }

        private bool _anyFileSelected;
        public bool AnyFileSelected
        {
            get { return _anyFileSelected; }
            set { _anyFileSelected = value; }
        }


        public event EventHandler FilesListLoaded;

        /// <summary>
        /// Opens file picker and populates files list
        /// </summary>
        public async void OpenDeployFiles()
        {
            FileOpenPicker openPicker = new FileOpenPicker();
            openPicker.ViewMode = PickerViewMode.List;
            openPicker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
            openPicker.FileTypeFilter.Add("*");
            openPicker.FileTypeFilter.Add(".hex");
            openPicker.FileTypeFilter.Add(".nmf");
            IReadOnlyList<StorageFile> files = await openPicker.PickMultipleFilesAsync();
            if (files.Count > 0)
            {
                // new list
                FilesList = new ObservableCollection<DeployFile>();

                // get each file and add it to collection
                for (int i = 0; i < files.Count; i++)
                {
                    var basicProp = await files[i].GetBasicPropertiesAsync();
                    // add new files
                    FilesList.Add(new DeployFile(files[i].DisplayName, files[i].Path, 0x0, 0x0, basicProp.ItemDate.ToString("g")));
                }
                FilesListLoaded?.Invoke(this, EventArgs.Empty);
            }
        }

        public void DeployFile()
        {
            bool success = true;

            // show busy
            BusySrv.ShowBusy();

            // get only selected files
            IEnumerable<DeployFile> deployList = FilesList.ToArray().Where(s => s.Selected == true);

            // TBD
            // the code to deploy file goes here...

            // end busy
            BusySrv.HideBusy();

            // show result to user
            if (success)
                DialogSrv.ShowMessageAsync(deployList.Count() > 1 ? Res.GetString("DP_FilesDeployed") : Res.GetString("DP_FileDeployed"));
            else
                DialogSrv.ShowMessageAsync(deployList.Count() > 1 ? Res.GetString("DP_FailToDeployFiles") : Res.GetString("DP_FailToDeployFile"));
        }
    }
}
