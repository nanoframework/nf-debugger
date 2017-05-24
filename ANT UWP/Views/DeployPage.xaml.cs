//
// Copyright (c) 2017 The nanoFramework project contributors
// See LICENSE file in the project root for full license information.
//
using NanoFramework.ANT.Models;
using NanoFramework.ANT.ViewModels;
using System;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Data;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=234238

namespace NanoFramework.ANT.Views
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class DeployPage : Page
    {
        // strongly-typed view models enable x:bind
        public DeployViewModel ViewModel => this.DataContext as DeployViewModel;
        public DeployPage()
        {
            this.InitializeComponent();

            // catch event of all files loaded to select them all
            ViewModel.FilesListLoaded += ViewModel_FilesListLoaded;
        }

        private void ViewModel_FilesListLoaded(object sender, EventArgs e)
        {
            // stop selection changed event
            filesListView.SelectionChanged -= filesListView_SelectionChanged;
            // default
            ViewModel.AnyFileSelected = false;
            // select all item as default
            for (int i = 0; i < filesListView.Items.Count; i++)
            {
                if (ViewModel.FilesList[i].Selected)
                {
                    filesListView.SelectedItems.Add(filesListView.Items[i]);
                    // any item selected, enable deploy button
                    ViewModel.AnyFileSelected = true;
                }
            }
            // back with selection changed event
            filesListView.SelectionChanged += filesListView_SelectionChanged;
        }

        private void filesListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // do we have any file?
            if((ViewModel.FilesList?.Count ?? 0) <= 0)
            {
                // disable deploy and delete buttons and exit
                ViewModel.AnyFileSelected = false;
                return;
            }

            // deselect item(s) on view model collection
            if ((e.RemovedItems?.Count ?? 0) > 0)
            {
                foreach (DeployFile df in e.RemovedItems)
                {
                    // if item exists
                    if(ViewModel.FilesList.IndexOf(df) > -1)
                        ViewModel.FilesList[ViewModel.FilesList.IndexOf(df)].Selected = false;
                }
            }
            // select item(s) on view model collection
            if ((e.AddedItems?.Count ?? 0) > 0)
            {
                foreach (DeployFile df in e.AddedItems)
                {
                    // if item exists
                    if (ViewModel.FilesList.IndexOf(df) > -1)
                        ViewModel.FilesList[ViewModel.FilesList.IndexOf(df)].Selected = true;
                }
            }

            // update deploy button state
            foreach (DeployFile df in ViewModel.FilesList)
            {
                if(df.Selected)
                {
                    ViewModel.AnyFileSelected = true;
                    return;
                }
            }
            // get here, then no files selected! disable button
            ViewModel.AnyFileSelected = false;
        }
    }
}
