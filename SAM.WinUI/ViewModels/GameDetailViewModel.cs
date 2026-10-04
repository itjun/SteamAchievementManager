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
using SAM.Core.Services;

namespace SAM.WinUIApp.ViewModels
{
    /// <summary>
    /// 游戏详情 ViewModel：Phase 4 骨架（库页导航目标），
    /// Phase 5 落地 SAM.Worker 统计子进程、成就/统计编辑与未保存拦截。
    /// </summary>
    public partial class GameDetailViewModel : ViewModelBase, IDisposable
    {
        private readonly GameService _GameService;
        private readonly MainViewModel _Main;

        public GameDetailViewModel(uint appId, string name, GameService gameService, MainViewModel main)
        {
            this.AppId = appId;
            this.GameName = name;
            this._GameService = gameService;
            this._Main = main;
        }

        public uint AppId { get; }

        [ObservableProperty]
        public partial string GameName { get; set; }

        /// <summary>返回游戏库（返回导航）。</summary>
        public void GoBack()
        {
            this._Main.ShowLibrary();
        }

        /// <summary>离开确认（Phase 5：未保存更改时弹 ContentDialog）。</summary>
        public bool TryConfirmLeave()
        {
            return true;
        }

        public void Dispose()
        {
            // Phase 5：Dispose StatsClient（关闭子进程 stdin 使其自然退出）。
        }
    }
}
