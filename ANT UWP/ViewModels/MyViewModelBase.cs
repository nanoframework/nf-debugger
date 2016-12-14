using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using MFDeploy.Services.BusyService;
using MFDeploy.Services.Dialog;
using Newtonsoft.Json;
using PropertyChanged;
using Template10.Common;
using Template10.Services.NavigationService;
using Windows.UI.Xaml.Navigation;

namespace MFDeploy.ViewModels
{
    [ImplementPropertyChanged]
    public abstract class MyViewModelBase : GalaSoft.MvvmLight.ViewModelBase, INavigable
    {
        [JsonIgnore]
        public IDispatcherWrapper Dispatcher { get; set; }
        [JsonIgnore]
        public INavigationService NavigationService { get; set; }
        [JsonIgnore]
        public IStateItems SessionState { get; set; }

        public virtual Task OnNavigatedFromAsync(IDictionary<string, object> pageState, bool suspending) { return Task.FromResult<object>(null); }
        public virtual Task OnNavigatedToAsync(object parameter, NavigationMode mode, IDictionary<string, object> state) { return Task.FromResult<object>(null); }
        public virtual Task OnNavigatingFromAsync(NavigatingEventArgs args) { return Task.FromResult<object>(null); }


        [JsonIgnore]
        public IBusyService BusySrv { get; set; }

        [JsonIgnore]
        public IMyDialogService DialogSrv {get; set;}


        public override void Cleanup()
        {
            // unregister from everything
            this.MessengerInstance.Unregister(this);
            base.Cleanup();
        }
    }
}
