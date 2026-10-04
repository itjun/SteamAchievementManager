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

using Microsoft.UI.Windowing;
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

            // 未保存更改时拦截窗口关闭（AppWindow.Closing deferral + 异步确认）。
            this.AppWindow.Closing += this.OnAppWindowClosing;
        }

        private bool _SkipCloseInterception;

        /// <summary>
        /// 未保存更改时拦截窗口关闭：AppWindowClosingEventArgs 无 deferral，
        /// 用"先 Cancel 再异步确认、确认后置跳过标志二次关闭"的标准模式。
        /// </summary>
        private async void OnAppWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
        {
            if (this._SkipCloseInterception == true)
            {
                return;
            }

            if (this._MainViewModel.CurrentView is not GameDetailViewModel detail || detail.HasChanges == false)
            {
                return;
            }

            args.Cancel = true;

            if (await detail.ConfirmLeaveAsync() == true)
            {
                detail.Dispose();
                this._SkipCloseInterception = true;
                this.Close();
            }
        }

        /// <summary>接入游戏库：侧栏类型分类菜单（InfoBadge 计数）随库数据重建。</summary>
        public void AttachLibrary(GameLibraryViewModel library)
        {
            library.CategoriesChanged += (_, _) => this.RebuildCategoryItems(library);
            this.RebuildCategoryItems(library);
        }

        /// <summary>
        /// 侧栏分类重建：全部分类 + 页脚设置。原生 NavigationView 支持
        /// SelectedItem 双向选中（WPF-UI 需隧道事件 hack 的等价逻辑在此自然实现）。
        /// </summary>
        private void RebuildCategoryItems(GameLibraryViewModel library)
        {
            this.NavView.MenuItems.Clear();
            foreach (var category in library.Categories)
            {
                var item = new NavigationViewItem()
                {
                    Content = category.Label,
                    Tag = "category:" + category.Key,
                    Icon = new FontIcon() { Glyph = CategoryGlyph(category.Key) },
                    InfoBadge = category.Count > 0 ? new InfoBadge() { Value = category.Count } : null,
                };
                this.NavView.MenuItems.Add(item);
            }

            if (this.NavView.MenuItems.Count > 0)
            {
                this.NavView.SelectedItem = this.NavView.MenuItems[0];
            }
        }

        private static string CategoryGlyph(string key) => key switch
        {
            "all" => "\uE8A9",      // ViewAll
            "normal" => "\uE7FC",   // Library
            "demo" => "\uE768",     // Play
            "mod" => "\uE8EC",      // Tag
            "junk" => "\uE71D",     // AllApps
            _ => "\uE712",          // More
        };

        /// <summary>导航选中：类型分类（→游戏库页+筛选）/ 设置。</summary>
        private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (args.SelectedItemContainer?.Tag is not string tag)
            {
                return;
            }

            if (tag.StartsWith("category:", StringComparison.Ordinal) == true)
            {
                if (this.ContentFrame.CurrentSourcePageType != typeof(GameLibraryPage))
                {
                    this.ContentFrame.Navigate(typeof(GameLibraryPage), this._MainViewModel);
                }

                var key = tag["category:".Length..];
                var library = this._MainViewModel.Library;
                var category = library?.Categories.FirstOrDefault(candidate => candidate.Key == key);
                if (library != null && category != null)
                {
                    library.SelectedCategory = category;
                }

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
