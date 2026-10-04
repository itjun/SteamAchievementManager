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
using Microsoft.UI.Xaml.Input;
using SAM.WinUIApp.ViewModels;

namespace SAM.WinUIApp.Views.Controls
{
    public sealed partial class GameLibraryView : UserControl
    {
        public GameLibraryView()
        {
            this.InitializeComponent();

            // 虚拟化容器实现/复用时驱动图标懒加载：ContainerContentChanging 的
            // e.Item 直接给出数据项（Image.Loading 触发时 DataContext 尚未就绪）。
            this.GameGrid.ContainerContentChanging += this.OnGameGridContainerContentChanging;
        }

        private void OnGameGridContainerContentChanging(
            Microsoft.UI.Xaml.Controls.ListViewBase sender,
            ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue == false &&
                args.Item is GameItemViewModel item)
            {
                item.BeginLoadIcon();
            }
        }

        /// <summary>单击卡片打开游戏详情（Fluent 原生 ItemClick 模式；WinUI GridView 的 DoubleTapped 不触发）。</summary>
        private void OnGameGridItemClick(object sender, ItemClickEventArgs e)
        {
            if (e.ClickedItem is GameItemViewModel item &&
                this.DataContext is GameLibraryViewModel viewModel)
            {
                _ = viewModel.OpenGameCommand.ExecuteAsync(item);
            }
        }

        private void OnGameGridKeyDown(object sender, KeyRoutedEventArgs e)
        {
            if (e.Key == Windows.System.VirtualKey.Enter &&
                this.GameGrid.SelectedItem is GameItemViewModel item &&
                this.DataContext is GameLibraryViewModel viewModel)
            {
                _ = viewModel.OpenGameCommand.ExecuteAsync(item);
                e.Handled = true;
            }
        }
    }
}
