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

using System.Windows.Controls;
using System.Windows.Input;
using SAM.WpfApp.ViewModels;

namespace SAM.WpfApp.Views.Controls
{
    public partial class GameLibraryView : UserControl
    {
        public GameLibraryView()
        {
            this.InitializeComponent();
        }

        /// <summary>双击打开游戏（等价旧版 ListView.ItemActivate）。</summary>
        private void OnGameListDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is not System.Windows.DependencyObject source)
            {
                return;
            }

            if (this.DataContext is GameLibraryViewModel viewModel &&
                ItemsControl.ContainerFromElement(this.GameList, source) is ListViewItem)
            {
                viewModel.OpenGameCommand.Execute(this.GameList.SelectedItem);
            }
        }
    }
}
