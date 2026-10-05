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

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SAM.WinUIApp.ViewModels
{
    /// <summary>
    /// 主窗口 ViewModel：全局状态栏 + 当前内容视图（游戏库 ↔ 游戏详情）
    /// + Steam 不可用状态（未安装/未运行时应用照常启动，页面内提示）。
    /// </summary>
    public partial class MainViewModel : ViewModelBase
    {
        // WinUI 3：[ObservableProperty] 需用分部属性（WinRT/AOT 安全，MVVMTK0045）。
        [ObservableProperty]
        public partial string StatusText { get; set; } = "就绪";

        [ObservableProperty]
        public partial ViewModelBase? CurrentView { get; set; }

        [ObservableProperty]
        public partial bool SteamUnavailable { get; set; }

        [ObservableProperty]
        public partial string? SteamUnavailableTitle { get; set; }

        [ObservableProperty]
        public partial string? SteamUnavailableMessage { get; set; }

        public GameLibraryViewModel? Library { get; set; }

        /// <summary>页面"重试"按钮：请求 App 重新执行 Steam 初始化（无需重启应用）。</summary>
        public event Action? SteamRetryRequested;

        public void ShowLibrary()
        {
            this.CurrentView = this.Library;
        }

        [RelayCommand]
        private void RetrySteam()
        {
            this.SteamRetryRequested?.Invoke();
        }

        /// <summary>Steam 初始化失败：置页面内提示状态（应用继续运行，可稍后重试）。</summary>
        public void MarkSteamUnavailable(string title, string message)
        {
            this.SteamUnavailableTitle = title;
            this.SteamUnavailableMessage = message;
            this.SteamUnavailable = true;
            this.StatusText = "Steam 未就绪";
        }

        /// <summary>重试成功：清提示、回正常内容（CurrentView 随后指向游戏库）。</summary>
        public void ClearSteamUnavailable()
        {
            this.SteamUnavailable = false;
            this.StatusText = "就绪";
        }
    }
}
