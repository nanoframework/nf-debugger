//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using NanoFramework.ANT.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace NanoFramework.ANT.Services.StorageService
{
    public class StorageInterfaceService : IStorageInterfaceService
    {
        public string DeployFolderToken { get { return "DeployFolderToken"; } }

        public bool IsDeployFolderAvailable { get { return Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.ContainsItem(DeployFolderToken); } }

        public async Task<StorageFolder> GetDeployFolder()
        {
            StorageFolder stFolder;
            // check if token exists
            if (Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.ContainsItem(DeployFolderToken))
            {
                // yes, get folder
                stFolder = await Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.GetFolderAsync(DeployFolderToken);
            }
            else
            {
                // no, return null
                stFolder = null;
            }
            return stFolder;
        }

        public async Task<string> PickDeployFolder()
        {
            // prepare folder picker
            FolderPicker folderPicker = new FolderPicker();
            folderPicker.SuggestedStartLocation = PickerLocationId.DocumentsLibrary;
            folderPicker.FileTypeFilter.Add("*");
            // open picker
            StorageFolder folder = await folderPicker.PickSingleFolderAsync();
            // any folder picked
            if (folder != null)
            {
                // save it to access list for future use
                Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.AddOrReplace(DeployFolderToken, folder);
                // return folder path
                return folder.Path;
            }
            // user canceled picker
            return String.Empty;
        }

        public async Task<IReadOnlyList<StorageFile>> GetDeployFiles()
        {
            // check if folder is available
            if (IsDeployFolderAvailable)
            {
                // get worker folder
                StorageFolder deployfolder = await Windows.Storage.AccessCache.StorageApplicationPermissions.FutureAccessList.GetFolderAsync(DeployFolderToken);

                // get files from worker folder
                IReadOnlyList<StorageFile> fileList = await deployfolder.GetFilesAsync();

                return fileList;
            }
            // worker folder unavailable
            return null;
        }
    }
}
