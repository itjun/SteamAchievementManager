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

using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using SAM.Core.Models;
using SAM.Core.Services;

namespace SAM.WinUIApp.ViewModels
{
    /// <summary>
    /// 单个游戏条目。图标显式异步加载：GridView 容器实现（虚拟化）时由
    /// 视图的 Loading 事件触发 BeginLoadIcon——弃用 WPF 版 getter 副作用模式
    /// （WinUI 绑定求值时机不同，且 BitmapImage 必须在 UI 线程创建）。
    /// [ObservableProperty] 一律用分部属性（WinRT 类型封送安全，MVVMTK0045）。
    /// </summary>
    public partial class GameItemViewModel : ObservableObject
    {
        private readonly GameService _GameService;
        private readonly DispatcherQueue _Dispatcher;

        private bool _IconRequested;

        public GameItemViewModel(GameInfo game, GameService gameService)
        {
            this.Game = game;
            this._GameService = gameService;
            this._Dispatcher = DispatcherQueue.GetForCurrentThread();
            this.Name = game.Name;
        }

        public GameInfo Game { get; }

        /// <summary>UIA 名称（卡片对等项的 Name 来自 ToString）。</summary>
        public override string ToString() => this.Name;

        public uint Id => this.Game.Id;

        /// <summary>卡片图标（懒加载，视图容器实现时触发）。</summary>
        [ObservableProperty]
        public partial BitmapImage? Icon { get; set; }

        /// <summary>家庭共享游戏（所有者为家庭成员）→ 卡片"家庭共享"徽章；由探测结果回填。</summary>
        [ObservableProperty]
        public partial bool IsFamilyShared { get; set; }

        /// <summary>本地已下载（InstalledGamesService 扫描结果）→ "已下载"弱徽标。</summary>
        [ObservableProperty]
        public partial bool IsInstalled { get; set; }

        /// <summary>探测结果回填（FamilySharingService），徽章随 INPC 即时出现。</summary>
        public void SetFamilyShared(bool value)
        {
            this.IsFamilyShared = value;
        }

        public void SetInstalled(bool value)
        {
            this.IsInstalled = value;
        }

        [ObservableProperty]
        public partial string Name { get; set; }

        public string TypeLabel => this.Game.Type switch
        {
            "normal" => "游戏",
            "demo" => "试玩",
            "mod" => "Mod",
            "junk" => "杂项",
            _ => "其他",
        };

        /// <summary>技术性元数据（三级信息）；类型语义由 TypeLabel 徽标单独承载。</summary>
        public string Subtitle =>
            $"App ID {this.Id.ToString(CultureInfo.InvariantCulture)}";

        /// <summary>卡片图标异步加载入口（视图容器实现时调用；等价旧版按可见性下载）。</summary>
        public void BeginLoadIcon()
        {
            if (this._IconRequested == true)
            {
                return;
            }

            this._IconRequested = true;
            _ = this.LoadIconAsync();
        }

        private async Task LoadIconAsync()
        {
            try
            {
                // GetImageUrl 含原生调用，须在 UI 线程执行（容器实现线程）。
                var url = this._GameService.GetImageUrl(this.Id);
                if (url == null)
                {
                    return;
                }

                var bytes = await this._GameService.GetIconAsync(url);
                if (bytes == null)
                {
                    return;
                }

                // BitmapImage 是 DependencyObject，必须在 UI 线程创建后通知绑定。
                this._Dispatcher.TryEnqueue(() =>
                {
                    this.Icon = CreateBitmap(bytes);
                });
            }
            catch
            {
                // 单个图标失败不影响列表（旧版同样静默保留占位）。
            }
        }

        /// <summary>App 元数据变更（回调 1001）：更新名称并重载图标。</summary>
        internal void RefreshData()
        {
            this.Name = this._GameService.GetAppName(this.Id) ??
                "App " + this.Id.ToString(CultureInfo.InvariantCulture);

            this.Icon = null;
            this._IconRequested = false;
        }

        internal static BitmapImage CreateBitmap(byte[] bytes)
        {
            var image = new BitmapImage();
            using (var stream = new MemoryStream(bytes).AsRandomAccessStream())
            {
                image.SetSourceAsync(stream).AsTask().GetAwaiter().GetResult();
            }

            return image;
        }
    }
}
