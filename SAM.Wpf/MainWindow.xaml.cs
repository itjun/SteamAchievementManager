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
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using SAM.WpfApp.Models;
using SAM.WpfApp.Services;
using SAM.WpfApp.ViewModels;
using SAM.WpfApp.Views.Pages;
using Wpf.Ui.Controls;

namespace SAM.WpfApp
{
    public partial class MainWindow : FluentWindow
    {
        /// <summary>分类 Key → 侧栏图标（视图侧映射，ViewModel 不接触 WPF-UI 类型）。</summary>
        private static readonly Dictionary<string, SymbolRegular> CategoryIcons = new()
        {
            ["all"] = SymbolRegular.AppsList24,
            ["normal"] = SymbolRegular.Games24,
            ["demo"] = SymbolRegular.Play24,
            ["mod"] = SymbolRegular.PuzzlePiece24,
            ["junk"] = SymbolRegular.Archive24,
            ["other"] = SymbolRegular.Box24,
        };

        private readonly GameLibraryViewModel _Library;
        private readonly ObservableCollection<object> _CategoryItems = new();

        public MainWindow(PageService pageService, ThemeService themeService, ThemeMode initialTheme, MainViewModel mainViewModel)
        {
            this.InitializeComponent();
            this.DataContext = mainViewModel;

            this._Library = mainViewModel.Library
                ?? throw new InvalidOperationException("主窗口创建前必须先初始化游戏库 ViewModel。");
            this._Library.CategoriesChanged += this.OnCategoriesChanged;
            this.RootNavigation.MenuItemsSource = this._CategoryItems;
            this.RebuildCategoryItems();

            // 有未保存修改时阻止直接关闭。
            this.Closing += (_, args) =>
            {
                if (mainViewModel.CurrentView is GameDetailViewModel detail &&
                    detail.HasChanges == true &&
                    System.Windows.MessageBox.Show(
                        "当前游戏有未保存的修改。\n确定放弃这些修改并退出？",
                        "Steam 成就管理器",
                        System.Windows.MessageBoxButton.YesNo,
                        System.Windows.MessageBoxImage.Warning) != System.Windows.MessageBoxResult.Yes)
                {
                    args.Cancel = true;
                }
            };

            // WPF-UI 4.x：页面提供器挂在 NavigationView 上（FluentWindow 不实现 INavigationWindow）。
            this.RootNavigation.SetPageProviderService(pageService);

            // 应用主题需在窗口创建后进行（Mica 背景依赖窗口句柄）。
            this.Loaded += (sender, _) =>
            {
                themeService.Apply(initialTheme, this);
                this.RootNavigation.Navigate(typeof(GameLibraryPage), null);
            };
        }

        /// <summary>
        /// 库加载/刷新/手动添加后重建分类菜单（ViewModel 已保证在 UI 线程触发）。
        /// 随后重新导航到游戏库页：WPF-UI 4.3 没有公开的程序化选中 API，
        /// Navigate 按页面类型自动选中首个匹配菜单项（全部），与重建后
        /// 重置为“全部”的 SelectedCategory 保持一致。
        /// </summary>
        private void OnCategoriesChanged(object? sender, EventArgs e)
        {
            if (this.Dispatcher.CheckAccess() == false)
            {
                this.Dispatcher.BeginInvoke((Action)(() => this.OnCategoriesChanged(sender, e)));
                return;
            }

            this.RebuildCategoryItems();
            this.RootNavigation.Navigate(typeof(GameLibraryPage), null);
        }

        private void RebuildCategoryItems()
        {
            this._CategoryItems.Clear();

            foreach (var category in this._Library.Categories)
            {
                var item = new NavigationViewItem
                {
                    Content = category.DisplayName,
                    TargetPageType = typeof(GameLibraryPage),
                    Tag = category.Key,
                    Icon = new SymbolIcon(CategoryIcons.GetValueOrDefault(category.Key, SymbolRegular.Box24)),
                };
                // Preview 隧道事件保证先于 WPF-UI 内部点击处理（导航）拿到分类；
                // 点击仍会自动导航到（缓存的）游戏库页，并选中该菜单项。
                item.PreviewMouseLeftButtonDown += this.OnCategoryItemPreviewMouseLeftButtonDown;
                this._CategoryItems.Add(item);
            }
        }

        private void OnCategoryItemPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is NavigationViewItem { Tag: string key })
            {
                var category = this._Library.Categories.FirstOrDefault(candidate => candidate.Key == key);
                if (category != null)
                {
                    this._Library.SelectedCategory = category;
                }
            }
        }
    }
}
