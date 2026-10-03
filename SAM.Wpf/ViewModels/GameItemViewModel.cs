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

using System.Globalization;
using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SAM.Core.Models;
using SAM.Core.Services;

namespace SAM.WpfApp.ViewModels
{
    /// <summary>单个游戏条目。图标懒加载：仅在列表行被实现（虚拟化）时求值 Icon 绑定。</summary>
    public partial class GameItemViewModel : ObservableObject
    {
        private readonly GameService _GameService;

        private BitmapSource? _Icon;
        private bool _IconRequested;

        public GameItemViewModel(GameInfo game, GameService gameService)
        {
            this.Game = game;
            this._GameService = gameService;
            this._Name = game.Name;
        }

        public GameInfo Game { get; }

        public uint Id => this.Game.Id;

        /// <summary>家庭共享游戏（所有者为家庭成员）→ 列表行"家庭共享"徽章；由探测结果回填。</summary>
        [ObservableProperty]
        private bool _IsFamilyShared;

        /// <summary>本地已下载（InstalledGamesService 扫描结果）→ "已下载"弱徽标。</summary>
        [ObservableProperty]
        private bool _IsInstalled;

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
        private string _Name;

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

        /// <summary>
        /// 懒加载图标（等价旧版按可见性下载，GamePicker.cs:517-532）：
        /// 首次求值触发异步加载，完成后通过 PropertyChanged 通知 UI 刷新。
        /// </summary>
        public BitmapSource? Icon
        {
            get
            {
                if (this._IconRequested == false)
                {
                    this._IconRequested = true;
                    _ = this.LoadIconAsync();
                }
                return this._Icon;
            }
        }

        private async Task LoadIconAsync()
        {
            try
            {
                // GetImageUrl 含原生调用，首段在 UI 线程执行（绑定求值线程）。
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

                this._Icon = CreateFrozenBitmap(bytes);
                this.OnPropertyChanged(nameof(this.Icon));
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

            this._Icon = null;
            this._IconRequested = false;
            this.OnPropertyChanged(nameof(this.Icon));
        }

        internal static BitmapSource CreateFrozenBitmap(byte[] bytes)
        {
            var image = new BitmapImage();
            using (var stream = new MemoryStream(bytes))
            {
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.StreamSource = stream;
                image.EndInit();
            }
            image.Freeze();
            return image;
        }
    }
}
