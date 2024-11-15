//
// Copyright (c) .NET Foundation and Contributors
// See LICENSE file in the project root for full license information.
//

using CommunityToolkit.Mvvm.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Serial_Test_App_WPF.ViewModel;

namespace Serial_Test_App_WPF.ViewModel
{
    public class ViewModelLocator
    {
        private static bool _isConfigured = false;
        private static readonly object _lock = new object();

        public ViewModelLocator()
        {
            if (!_isConfigured)
            {
                lock (_lock)
                {
                    if (!_isConfigured)
                    {
                        Ioc.Default.ConfigureServices(
                            new ServiceCollection()
                                .AddSingleton<MainViewModel>()
                                .BuildServiceProvider());
                        _isConfigured = true;
                    }
                }
            }
        }

        public MainViewModel Main => Ioc.Default.GetService<MainViewModel>();
    }
}
