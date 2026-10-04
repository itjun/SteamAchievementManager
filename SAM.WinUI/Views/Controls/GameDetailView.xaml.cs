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
using SAM.WinUIApp.ViewModels;

namespace SAM.WinUIApp.Views.Controls
{
    public sealed partial class GameDetailView : UserControl
    {
        public GameDetailView()
        {
            this.InitializeComponent();

            // 虚拟化容器实现/复用时驱动成就图标懒加载（同库页模式）。
            this.AchievementList.ContainerContentChanging += this.OnAchievementContainerChanging;
        }

        private void OnAchievementContainerChanging(
            Microsoft.UI.Xaml.Controls.ListViewBase sender,
            ContainerContentChangingEventArgs args)
        {
            if (args.InRecycleQueue == false &&
                args.Item is AchievementItemViewModel item)
            {
                item.BeginLoadIcon();
            }
        }
    }
}
