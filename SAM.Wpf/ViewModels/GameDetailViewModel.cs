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
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SAM.Core.GameStats;
using SAM.Core.Services;

namespace SAM.WpfApp.ViewModels
{
    /// <summary>
    /// 游戏详情 ViewModel（成就 + 统计 + Action Bar）。
    /// 数据经隐藏的统计工作子进程获取/提交（Steam 的 ISteamUserStats 绑定
    /// 进程 SteamAppId 上下文，同进程重建 Client 实验证不可行）。
    /// 保存语义逐字保留旧版 OnStore：仅提交差异项，任一失败立即中止，
    /// 无论成败最后整体刷新。
    /// </summary>
    public partial class GameDetailViewModel : ViewModelBase
    {
        private readonly uint _AppId;
        private readonly GameService _GameService;
        private readonly MainViewModel _Main;
        private readonly StatsClient _Client;

        private List<AchievementItemViewModel> _AllAchievements = new();

        [ObservableProperty]
        private ObservableCollection<AchievementItemViewModel> _Achievements = new();

        [ObservableProperty]
        private ObservableCollection<StatisticRowViewModel> _Statistics = new();

        [ObservableProperty]
        private string _GameName = "";

        [ObservableProperty]
        private string _AppIdText = "";

        [ObservableProperty]
        private string? _SearchText;

        [ObservableProperty]
        private bool _FilterAll = true;

        [ObservableProperty]
        private bool _FilterUnlocked;

        [ObservableProperty]
        private bool _FilterLocked;

        [ObservableProperty]
        private bool _ShowAchievements = true;

        [ObservableProperty]
        private bool _ShowStatistics;

        [ObservableProperty]
        private bool _IsBusy;

        [ObservableProperty]
        private string? _ErrorText;

        [ObservableProperty]
        private bool _HasChanges;

        [ObservableProperty]
        private int _ChangedCount;

        [ObservableProperty]
        private bool _StatsEditingEnabled;

        [ObservableProperty]
        private int _UnlockedCount;

        [ObservableProperty]
        private int _TotalCount;

        public string ProgressText => $"已解锁 {this.UnlockedCount} / {this.TotalCount}";

        partial void OnUnlockedCountChanged(int value) =>
            this.OnPropertyChanged(nameof(this.ProgressText));

        partial void OnTotalCountChanged(int value) =>
            this.OnPropertyChanged(nameof(this.ProgressText));

        public GameDetailViewModel(uint appId, string gameName, GameService gameService, MainViewModel main)
        {
            this._AppId = appId;
            this._GameService = gameService;
            this._Main = main;
            this._Client = new StatsClient(appId);

            this.GameName = gameName;
            this.AppIdText = "App ID " + appId.ToString(CultureInfo.InvariantCulture);

            _ = this.LoadAsync();
        }

        public bool TryConfirmLeave()
        {
            if (this.HasChanges == false)
            {
                return true;
            }

            return MessageBox.Show(
                "当前游戏有未保存的修改。\n确定放弃这些修改并离开？",
                "Steam 成就管理器",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) == MessageBoxResult.Yes;
        }

        public void Dispose()
        {
            this._Client.Dispose();
        }

        // ---- 加载 ----

        [RelayCommand]
        private async Task RefreshAsync() => await this.LoadAsync();

        private async Task LoadAsync()
        {
            if (this.IsBusy == true)
            {
                return;
            }

            this.IsBusy = true;
            this.ErrorText = null;
            this._Main.StatusText = $"正在读取「{this.GameName}」的成就与统计...";

            try
            {
                var response = await this._Client.GetAsync();
                if (response.Payload != null)
                {
                    this.ApplyPayload(response.Payload);
                }

                if (response.Ok == false)
                {
                    this.ErrorText = TranslateWorkerError(response.Error);
                    this._Main.StatusText = "读取失败";
                    MessageBox.Show(this.ErrorText, "Steam 成就管理器", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    this._Main.StatusText = $"{this.GameName}：成就 {this.UnlockedCount}/{this.TotalCount}，统计 {this.Statistics.Count} 项";
                }
            }
            catch (Exception exception)
            {
                this.ErrorText = "无法与统计工作进程通信：" + exception.Message;
                this._Main.StatusText = "读取失败";
                MessageBox.Show(this.ErrorText, "Steam 成就管理器", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                this.IsBusy = false;
            }
        }

        private void ApplyPayload(GameStatsPayload payload)
        {
            foreach (var old in this._AllAchievements)
            {
                old.PropertyChanged -= this.OnItemChanged;
            }
            foreach (var old in this.Statistics)
            {
                old.PropertyChanged -= this.OnItemChanged;
            }

            this._AllAchievements = payload.Achievements
                .Select(a => new AchievementItemViewModel(a, this._AppId, this._GameService))
                .ToList();
            foreach (var item in this._AllAchievements)
            {
                item.PropertyChanged += this.OnItemChanged;
            }

            var rows = payload.Stats.Select(s => new StatisticRowViewModel(s)).ToList();
            foreach (var row in rows)
            {
                row.PropertyChanged += this.OnItemChanged;
                row.IsEditable = this.StatsEditingEnabled && row.IsProtected == false;
            }
            this.Statistics = new ObservableCollection<StatisticRowViewModel>(rows);

            this.TotalCount = this._AllAchievements.Count;
            this.UnlockedCount = this._AllAchievements.Count(a => a.Data.IsAchieved);

            this.ApplyAchievementFilter();
            this.RecomputeHasChanges();
        }

        private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(AchievementItemViewModel.PendingAchieved)
                or nameof(StatisticRowViewModel.EditValue)
                or nameof(StatisticRowViewModel.ErrorText))
            {
                this.RecomputeHasChanges();
            }
        }

        private void RecomputeHasChanges()
        {
            int count = this._AllAchievements.Count(a => a.IsChanged) +
                this.Statistics.Count(s => s.IsEdited);
            this.ChangedCount = count;
            this.HasChanges = count > 0;
            this.SaveCommand.NotifyCanExecuteChanged();
        }

        partial void OnIsBusyChanged(bool value) => this.SaveCommand.NotifyCanExecuteChanged();

        private bool CanSave() => this.HasChanges && this.IsBusy == false;

        // ---- 保存（旧版 OnStore 语义）----

        [RelayCommand(CanExecute = nameof(CanSave))]
        private async Task SaveAsync()
        {
            if (this.CanSave() == false)
            {
                return;
            }

            var achievementChanges = this._AllAchievements
                .Where(a => a.IsChanged)
                .Select(a => new AchievementChange() { Name = a.Data.Id, Achieved = a.PendingAchieved })
                .ToList();
            var statChanges = this.Statistics
                .Select(s => s.BuildChange())
                .Where(c => c != null)
                .Cast<StatChange>()
                .ToList();

            if (achievementChanges.Count == 0 && statChanges.Count == 0)
            {
                this.RecomputeHasChanges();
                return;
            }

            this.IsBusy = true;
            this._Main.StatusText = "正在保存修改...";

            try
            {
                var response = await this._Client.StoreAsync(achievementChanges, statChanges);
                if (response.Payload != null)
                {
                    this.ApplyPayload(response.Payload);
                }

                if (response.Ok == true)
                {
                    MessageBox.Show(
                        $"已保存 {achievementChanges.Count} 项成就与 {statChanges.Count} 项统计。",
                        "Steam 成就管理器",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    this._Main.StatusText = $"{this.GameName}：已保存修改";
                }
                else
                {
                    MessageBox.Show(
                        "保存失败，已中止：" + TranslateWorkerError(response.Error),
                        "Steam 成就管理器",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    this._Main.StatusText = "保存失败";
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "保存失败：无法与统计工作进程通信。\n" + exception.Message,
                    "Steam 成就管理器",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                this._Main.StatusText = "保存失败";
            }
            finally
            {
                this.IsBusy = false;
            }
        }

        // ---- 重置（旧版三重确认）----

        [RelayCommand]
        private async Task ResetAllAsync()
        {
            if (MessageBox.Show(
                    "确定要重置该游戏的统计吗？",
                    "重置统计",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                return;
            }

            bool achievementsToo = MessageBox.Show(
                "是否同时重置成就？\n（警告：已解锁成就将被撤销，解锁时间无法恢复）",
                "重置统计",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;

            if (MessageBox.Show(
                    "真的确定吗？此操作不可撤销！",
                    "重置统计",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Error) != MessageBoxResult.Yes)
            {
                return;
            }

            this.IsBusy = true;
            this._Main.StatusText = "正在重置...";

            try
            {
                var response = await this._Client.ResetAllAsync(achievementsToo);
                if (response.Payload != null)
                {
                    this.ApplyPayload(response.Payload);
                }

                if (response.Ok == false)
                {
                    MessageBox.Show(
                        "重置失败：" + TranslateWorkerError(response.Error),
                        "Steam 成就管理器",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    this._Main.StatusText = $"{this.GameName}：已重置";
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    "重置失败：无法与统计工作进程通信。\n" + exception.Message,
                    "Steam 成就管理器",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                this.IsBusy = false;
            }
        }

        // ---- 批量操作（旧版对可见项生效）----

        [RelayCommand]
        private void UnlockAll() => this.BatchSet(value: true);

        [RelayCommand]
        private void LockAll() => this.BatchSet(value: false);

        [RelayCommand]
        private void InvertAll()
        {
            int skipped = 0;
            foreach (var item in this.Achievements)
            {
                if (item.CanToggle == false)
                {
                    skipped++;
                    continue;
                }
                item.PendingAchieved = item.PendingAchieved == false;
            }
            this.ReportBatchSkipped(skipped);
        }

        private void BatchSet(bool value)
        {
            int skipped = 0;
            foreach (var item in this.Achievements)
            {
                if (item.CanToggle == false)
                {
                    skipped++;
                    continue;
                }
                item.PendingAchieved = value;
            }
            this.ReportBatchSkipped(skipped);
        }

        private void ReportBatchSkipped(int skipped)
        {
            if (skipped > 0)
            {
                MessageBox.Show(
                    $"已跳过 {skipped} 项受保护成就。",
                    "Steam 成就管理器",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        // ---- 导航 / 过滤 ----

        [RelayCommand]
        private void GoBack()
        {
            if (this.TryConfirmLeave() == false)
            {
                return;
            }

            this.Dispose();
            this._Main.ShowLibrary();
        }

        partial void OnSearchTextChanged(string? value) => this.ApplyAchievementFilter();

        partial void OnFilterAllChanged(bool value)
        {
            if (value == true)
            {
                this.FilterUnlocked = false;
                this.FilterLocked = false;
                this.ApplyAchievementFilter();
            }
        }

        partial void OnFilterUnlockedChanged(bool value)
        {
            if (value == true)
            {
                this.FilterAll = false;
                this.FilterLocked = false;
                this.ApplyAchievementFilter();
            }
        }

        partial void OnFilterLockedChanged(bool value)
        {
            if (value == true)
            {
                this.FilterAll = false;
                this.FilterUnlocked = false;
                this.ApplyAchievementFilter();
            }
        }

        partial void OnShowAchievementsChanged(bool value)
        {
            if (value == true && this.ShowStatistics == true)
            {
                this.ShowStatistics = false;
            }
        }

        partial void OnShowStatisticsChanged(bool value)
        {
            if (value == true && this.ShowAchievements == true)
            {
                this.ShowAchievements = false;
            }
        }

        partial void OnStatsEditingEnabledChanged(bool value)
        {
            foreach (var row in this.Statistics)
            {
                row.IsEditable = value && row.IsProtected == false;
            }
        }

        private void ApplyAchievementFilter()
        {
            var search = string.IsNullOrEmpty(this.SearchText) == true ? null : this.SearchText;

            var filter = this.FilterUnlocked == true
                ? AchievementFilter.Unlocked
                : this.FilterLocked == true
                    ? AchievementFilter.Locked
                    : AchievementFilter.All;

            this.Achievements = new ObservableCollection<AchievementItemViewModel>(
                this._AllAchievements.Where(
                    a => AchievementService.MatchesFilter(a.Data, a.PendingAchieved, search, filter)));
        }

        internal static string TranslateWorkerError(string? error)
        {
            if (string.IsNullOrEmpty(error) == true)
            {
                return "未知错误";
            }

            if (error.Contains("schema file not found") == true)
            {
                return "未找到本地 schema 文件。请先运行一次该游戏（或让 Steam 同步其统计数据）后再试。";
            }

            if (error.Contains("timeout") == true)
            {
                return "等待 Steam 响应超时。";
            }

            if (error.Contains("result=2") == true)
            {
                return "通常这意味着你不拥有该游戏。";
            }

            if (error.Contains("steam init failed") == true)
            {
                return "统计工作进程初始化 Steam 失败：" + error;
            }

            return error;
        }
    }
}
