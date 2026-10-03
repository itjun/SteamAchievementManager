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

using CommunityToolkit.Mvvm.ComponentModel;

namespace SAM.WpfApp.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel：全局状态栏 + 当前内容视图（游戏库 ↔ 游戏详情，Phase 6 统一导航）。
    /// </summary>
    public partial class MainViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _StatusText = "就绪";

        [ObservableProperty]
        private ViewModelBase? _CurrentView;

        public GameLibraryViewModel? Library { get; set; }

        public void ShowLibrary()
        {
            this.CurrentView = this.Library;
        }
    }
}
