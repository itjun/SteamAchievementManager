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

using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using SAM.Core.GameStats;
using SAM.Core.Services;

namespace SAM.WpfApp.ViewModels
{
    /// <summary>
    /// 单条成就。PendingAchieved 为编辑态（对应旧版 ListViewItem.Checked），
    /// Data.IsAchieved 为 Steam 侧状态；IsChanged = 两者不一致（提交依据）。
    /// </summary>
    public partial class AchievementItemViewModel : ObservableObject
    {
        private readonly GameService _GameService;
        private readonly uint _AppId;

        private BitmapSource? _Icon;
        private bool _IconRequested;

        public AchievementItemViewModel(AchievementData data, uint appId, GameService gameService)
        {
            this.Data = data;
            this._AppId = appId;
            this._GameService = gameService;
            this._PendingAchieved = data.IsAchieved;
        }

        public AchievementData Data { get; }

        public string Id => this.Data.Id;

        /// <summary>未本地化的名称（# 开头 token）回退显示 Id（旧版 Manager.cs:514-518）。</summary>
        public string DisplayName =>
            string.IsNullOrEmpty(this.Data.Name) == false &&
            this.Data.Name.StartsWith("#", StringComparison.InvariantCulture) == false
                ? this.Data.Name
                : this.Data.Id;

        public string DisplayDescription =>
            this.Data.Name.StartsWith("#", StringComparison.InvariantCulture) == true
                ? ""
                : this.Data.Description;

        public bool IsProtected => AchievementService.IsProtected(this.Data);

        public bool CanToggle => this.IsProtected == false;

        [ObservableProperty]
        private bool _PendingAchieved;

        public bool IsChanged => this.PendingAchieved != this.Data.IsAchieved;

        public string StateText => this.PendingAchieved == true ? "已解锁" : "未解锁";

        public string UnlockText => this.Data.IsAchieved == true && this.Data.UnlockTime > 0
            ? DateTimeOffset.FromUnixTimeSeconds(this.Data.UnlockTime).LocalDateTime.ToString()
            : "";

        /// <summary>懒加载图标（状态相关：已解锁用 IconNormal，未解锁用 IconLocked，空则回退）。</summary>
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
                var filename = this.PendingAchieved == true
                    ? this.Data.IconNormal
                    : this.Data.IconLocked ?? this.Data.IconNormal;
                if (filename == null)
                {
                    return;
                }

                var url = AchievementService.GetIconUrl(this._AppId, filename);
                if (url == null)
                {
                    return;
                }

                var bytes = await this._GameService.GetIconAsync(url);
                if (bytes == null)
                {
                    return;
                }

                this._Icon = GameItemViewModel.CreateFrozenBitmap(bytes);
                this.OnPropertyChanged(nameof(this.Icon));
            }
            catch
            {
                // 单个图标失败保留占位（与旧版一致）。
            }
        }
    }
}
