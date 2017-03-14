//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace NanoFramework.ANT.Services.StorageService
{
    public interface IStorageInterfaceService
    {
        /// <summary>
        /// Default token name for deploy folder.
        /// </summary>
        string DeployFolderToken { get; }
        /// <summary>
        /// Availability of a folder to work with deploy files.
        /// </summary>
        bool IsDeployFolderAvailable { get; }
        /// <summary>
        /// Get worker folder from storage FAL (Future Access List) for deploy files.
        /// </summary>
        /// <returns>folder or null if none in FAL</returns>
        Task<StorageFolder> GetDeployFolder();
        /// <summary>
        /// Pick a folder to work with deploy files and keep it in FAL (Future Access List).
        /// </summary>
        /// <returns>folder path if any picked</returns>
        Task<string> PickDeployFolder();
        /// <summary>
        /// Get all files from worker folder.
        /// </summary>
        /// <returns>List of files or null if no folder/files</returns>
        Task<IReadOnlyList<StorageFile>> GetDeployFiles();
    }
}
