/* Copyright (c) 2024 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would be
 *    appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using SAM.WinUIApp.Services;
using SAM.WinUIApp.ViewModels;
using SAM.WinUIApp.Views.Pages;

namespace SAM.WinUIApp
{
    public sealed partial class MainWindow : Window
    {
        private readonly MainViewModel _MainViewModel;
        private readonly SettingsService _SettingsService;
        private readonly ThemeService _ThemeService;

        public MainWindow(MainViewModel mainViewModel, SettingsService settingsService, ThemeService themeService)
        {
            this._MainViewModel = mainViewModel;
            this._SettingsService = settingsService;
            this._ThemeService = themeService;

            this.InitializeComponent();

            this.Title = "Steam 成就管理器";
            this.ExtendsContentIntoTitleBar = true;

            // Window 不是 FrameworkElement，没有 DataContext：挂到根 Grid 供状态栏等绑定继承。
            this.RootGrid.DataContext = mainViewModel;

            // 初始页：游戏库。
            this.ContentFrame.Navigate(typeof(GameLibraryPage), mainViewModel);
        }

        /// <summary>导航选中：游戏库 / 设置（Phase 4 起侧栏分类项也走这里）。</summary>
        private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer?.Tag is not string tag)
            {
                return;
            }

            switch (tag)
            {
                case "library":
                    if (this.ContentFrame.CurrentSourcePageType != typeof(GameLibraryPage))
                    {
                        this.ContentFrame.Navigate(typeof(GameLibraryPage), this._MainViewModel);
                    }

                    break;

                case "settings":
                    if (this.ContentFrame.CurrentSourcePageType != typeof(SettingsPage))
                    {
                        this.ContentFrame.Navigate(typeof(SettingsPage), (this._SettingsService, this._ThemeService));
                    }

                    break;
            }
        }
    }
}
