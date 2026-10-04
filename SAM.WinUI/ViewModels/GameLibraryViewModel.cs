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

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Dispatching;
using SAM.Core.Services;
using SAM.WinUIApp.Models;
using SAM.WinUIApp.Services;

namespace SAM.WinUIApp.ViewModels
{
    /// <summary>
    /// 游戏库 ViewModel（迁移自 WPF 版，行为保真）：
    /// 搜索/类型过滤、刷新、手动添加、AppDataChanged 名称更新；
    /// 类型过滤 = 侧栏分类单选（Categories/SelectedCategory，MainWindow 据此重建侧栏）。
    /// WinUI 适配：DispatcherQueue 替代 App.Current.Dispatcher；MessageBox → IDialogService；
    /// 分段筛选绑定用布尔包装属性（WinUI 无 Binding.DoNothing 语义的枚举转换器方案）。
    /// </summary>
    public partial class GameLibraryViewModel : ViewModelBase
    {
        /// <summary>games.xml 的已知类型（顺序即侧栏显示顺序）。</summary>
        private static readonly (string Key, string Label)[] KnownTypes =
        {
            ("normal", "游戏"),
            ("demo", "试玩"),
            ("mod", "Mod"),
            ("junk", "杂项"),
        };

        /// <summary>家庭共享探测并发度（对 Steam 客户端保持礼貌）。</summary>
        private const int ProbeConcurrency = 3;

        private readonly SteamService _SteamService;
        private readonly GameService _GameService;
        private readonly FamilySharingService _FamilySharing;
        private readonly InstalledGamesService _InstalledGames;
        private readonly LocalizedNameService _LocalizedNames;
        private readonly MainViewModel _Main;
        private readonly DialogService _Dialogs;

        private readonly DispatcherQueue _Dispatcher;

        private List<GameItemViewModel> _AllGames = new();

        private CancellationTokenSource? _ProbeCancellation;

        private CancellationTokenSource? _LocalizeCancellation;

        public GameLibraryViewModel(
            SteamService steamService,
            GameService gameService,
            FamilySharingService familySharing,
            InstalledGamesService installedGames,
            LocalizedNameService localizedNames,
            MainViewModel main,
            DialogService dialogs)
        {
            this._SteamService = steamService;
            this._GameService = gameService;
            this._FamilySharing = familySharing;
            this._InstalledGames = installedGames;
            this._LocalizedNames = localizedNames;
            this._Main = main;
            this._Dialogs = dialogs;
            this._Dispatcher = DispatcherQueue.GetForCurrentThread();

            // 回调泵在 UI 线程触发 RunCallbacks → 事件在 UI 线程，可直接操作集合。
            this._SteamService.AppDataChanged += this.OnAppDataChanged;

            _ = this.LoadAsync();
        }

        [ObservableProperty]
        public partial ObservableCollection<GameItemViewModel> Games { get; set; } = new();

        [ObservableProperty]
        public partial string? SearchText { get; set; }

        /// <summary>所有权筛选（自购/家庭共享），与侧栏类型分类正交。</summary>
        [ObservableProperty]
        public partial OwnershipFilter Ownership { get; set; }

        /// <summary>安装状态筛选（已下载/未下载），与所有权/类型筛选正交。</summary>
        [ObservableProperty]
        public partial InstallFilter Install { get; set; }

        [ObservableProperty]
        public partial GameCategory? SelectedCategory { get; set; }

        [ObservableProperty]
        public partial bool IsLoading { get; set; }

        /// <summary>games.xml 下载失败，已回退默认列表（原版静默回退 Spacewar）。</summary>
        [ObservableProperty]
        public partial bool UsedFallback { get; set; }

        [ObservableProperty]
        public partial string? EmptyMessage { get; set; }

        [ObservableProperty]
        public partial bool ShowEmpty { get; set; }

        /// <summary>侧栏分类列表（"全部" 恒有；四个固定类型恒显示，"其他"仅有数据时出现）。</summary>
        public IReadOnlyList<GameCategory> Categories { get; private set; } = Array.Empty<GameCategory>();

        /// <summary>加载/刷新/手动添加后触发，MainWindow 据此重建侧栏菜单。</summary>
        public event EventHandler? CategoriesChanged;

        // ===== 分段筛选的布尔包装（WinUI 无枚举双向转换器等价物；置 true 即选中该段） =====

        public bool OwnershipIsAll
        {
            get => this.Ownership == OwnershipFilter.All;
            set { if (value == true) { this.Ownership = OwnershipFilter.All; } }
        }

        public bool OwnershipIsOwned
        {
            get => this.Ownership == OwnershipFilter.Owned;
            set { if (value == true) { this.Ownership = OwnershipFilter.Owned; } }
        }

        public bool OwnershipIsShared
        {
            get => this.Ownership == OwnershipFilter.FamilyShared;
            set { if (value == true) { this.Ownership = OwnershipFilter.FamilyShared; } }
        }

        public bool InstallIsAll
        {
            get => this.Install == InstallFilter.All;
            set { if (value == true) { this.Install = InstallFilter.All; } }
        }

        public bool InstallIsInstalled
        {
            get => this.Install == InstallFilter.Installed;
            set { if (value == true) { this.Install = InstallFilter.Installed; } }
        }

        public bool InstallIsNotInstalled
        {
            get => this.Install == InstallFilter.NotInstalled;
            set { if (value == true) { this.Install = InstallFilter.NotInstalled; } }
        }

        private void OnAppDataChanged(uint appId)
        {
            var item = this._AllGames.FirstOrDefault(game => game.Id == appId);
            if (item == null)
            {
                return;
            }

            item.RefreshData();
        }

        [RelayCommand]
        private async Task LoadAsync()
        {
            if (this.IsLoading == true)
            {
                return;
            }

            this.IsLoading = true;
            this.ShowEmpty = false;
            this._Main.StatusText = "正在刷新游戏库...";

            try
            {
                var progress = new Progress<string>(message => this._Main.StatusText = message);
                var result = await this._GameService.LoadGamesAsync(progress);

                this._AllGames = result.Games
                    .Select(game => new GameItemViewModel(game, this._GameService))
                    .ToList();
                this.UsedFallback = result.UsedFallback;

                // ApplyFilter 在加载中会直接返回（防搜索抖动）；必须先退出加载态
                // 再套用筛选，否则首次加载完成后列表保持为空，直到用户手动搜索。
                this.IsLoading = false;
                this.RebuildCategories();
                this.ApplyFilter();
                this.ApplyFamilySharingFromCache();
                this.ApplyInstalledFromScan();

                var sharedCount = this._AllGames.Count(game => game.IsFamilyShared);
                this._Main.StatusText = result.UsedFallback == true
                    ? "游戏列表下载失败，已显示默认列表"
                    : sharedCount > 0
                        ? $"共 {this._AllGames.Count} 个游戏，其中 {sharedCount} 个家庭共享"
                        : $"共 {this._AllGames.Count} 个游戏";

                // 中文名回填：缓存命中即时生效，未命中的限频逐个取（不打断列表交互）。
                _ = this.LocalizeNamesAsync();
            }
            catch (Exception exception)
            {
                this.Games = new();
                this.EmptyMessage = $"加载失败：{exception.Message}";
                this.ShowEmpty = true;
                this._Main.StatusText = "加载游戏库失败";
            }
            finally
            {
                this.IsLoading = false;
            }
        }

        [RelayCommand]
        private Task RefreshAsync() => this.LoadAsync();

        partial void OnSearchTextChanged(string? value) => this.ApplyFilter();

        partial void OnSelectedCategoryChanged(GameCategory? value) => this.ApplyFilter();

        partial void OnOwnershipChanged(OwnershipFilter value)
        {
            this.NotifyFilterWrappers();
            this.ApplyFilter();
        }

        partial void OnInstallChanged(InstallFilter value)
        {
            this.NotifyFilterWrappers();
            this.ApplyFilter();
        }

        private void NotifyFilterWrappers()
        {
            this.OnPropertyChanged(nameof(this.OwnershipIsAll));
            this.OnPropertyChanged(nameof(this.OwnershipIsOwned));
            this.OnPropertyChanged(nameof(this.OwnershipIsShared));
            this.OnPropertyChanged(nameof(this.InstallIsAll));
            this.OnPropertyChanged(nameof(this.InstallIsInstalled));
            this.OnPropertyChanged(nameof(this.InstallIsNotInstalled));
        }

        /// <summary>
        /// 按当前库重算分类（含数量），并重置选中为“全部”：数据集变了从全集看起，
        /// 也与 MainWindow 重建菜单后选中首项（全部）的高亮一致。
        /// 四个固定类型无论数量多少恒显示（防侧栏项随刷新忽隐忽现）；
        /// "其他"归拢未知类型，仅有数据时出现。
        /// </summary>
        private void RebuildCategories()
        {
            var counts = this._AllGames
                .GroupBy(game => game.Game.Type)
                .ToDictionary(group => group.Key, group => group.Count());

            var categories = new List<GameCategory>()
            {
                new("all", "全部", this._AllGames.Count),
            };

            foreach (var (key, label) in KnownTypes)
            {
                categories.Add(new GameCategory(key, label, counts.GetValueOrDefault(key)));
            }

            var known = KnownTypes.Select(type => type.Key).ToHashSet();
            var other = counts
                .Where(pair => known.Contains(pair.Key) == false)
                .Sum(pair => pair.Value);
            if (other > 0)
            {
                categories.Add(new GameCategory("other", "其他", other));
            }

            this.Categories = categories;
            this.SelectedCategory = categories[0];

            this.CategoriesChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyFilter()
        {
            if (this.IsLoading == true)
            {
                return;
            }

            var search = string.IsNullOrEmpty(this.SearchText) == true ? null : this.SearchText;
            var categoryKey = this.SelectedCategory?.Key;

            var filtered = this._AllGames
                .Where(game => GameService.MatchesFilter(game.Game, search, categoryKey))
                .Where(game => this.Ownership switch
                {
                    OwnershipFilter.Owned => game.IsFamilyShared == false,
                    OwnershipFilter.FamilyShared => game.IsFamilyShared,
                    _ => true,
                })
                .Where(game => this.Install switch
                {
                    InstallFilter.Installed => game.IsInstalled,
                    InstallFilter.NotInstalled => game.IsInstalled == false,
                    _ => true,
                })
                .OrderBy(game => game.Name, StringComparer.CurrentCulture)
                .ToList();

            this.Games = new ObservableCollection<GameItemViewModel>(filtered);

            this.EmptyMessage = filtered.Count == 0 ? this.BuildEmptyMessage(search) : null;
            this.ShowEmpty = filtered.Count == 0;

            this._Main.StatusText = $"显示 {filtered.Count} / {this._AllGames.Count} 个游戏";
        }

        /// <summary>空态文案：搜索优先，其次所有权/安装状态（为空给出针对性提示）。</summary>
        private string BuildEmptyMessage(string? search)
        {
            if (search != null)
            {
                return $"没有匹配“{search}”的游戏";
            }

            if (this.Ownership == OwnershipFilter.FamilyShared)
            {
                return "库中没有家庭共享的游戏";
            }

            if (this.Install == InstallFilter.Installed)
            {
                return "没有已下载到本机的游戏";
            }

            return "当前筛选条件下没有游戏";
        }

        /// <summary>
        /// 家庭共享检测：仅手动触发（DetectFamilySharingCommand）。
        /// 原因：探测会让 Steam 把游戏短暂标记为"运行中"，可能触发已安装游戏的
        /// 自动更新排队（实测引发过一轮批量下载）——启动时只读缓存回填徽章
        /// （ApplyFamilySharingFromCache），子进程探测只在你点了按钮后进行。
        /// </summary>
        [RelayCommand]
        private async Task DetectFamilySharingAsync()
        {
            this._ProbeCancellation?.Cancel();
            this._ProbeCancellation?.Dispose();
            this._ProbeCancellation = new CancellationTokenSource();
            var cancellationToken = this._ProbeCancellation.Token;

            var steamId = this._SteamService.GetSteamId();
            var byId = this._AllGames.ToDictionary(game => game.Id);

            // 手动检测 = 全量强制重探（覆盖缓存）。
            var pending = this._AllGames.Select(game => game.Id).ToList();
            this._Main.StatusText = $"正在检测家庭共享（0/{pending.Count}）...";

            using var gate = new SemaphoreSlim(ProbeConcurrency, ProbeConcurrency);
            var completed = 0;
            var failures = 0;
            var tasks = pending.Select(async appId =>
            {
                await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var result = await this._FamilySharing.ProbeAsync(appId, cancellationToken).ConfigureAwait(false);
                    if (result == null)
                    {
                        System.Threading.Interlocked.Increment(ref failures);
                        return;
                    }

                    this._FamilySharing.Store(steamId, appId, result.Value);
                    var done = System.Threading.Interlocked.Increment(ref completed);
                    // 回到 UI 线程更新条目与状态栏。
                    this._Dispatcher.TryEnqueue(() =>
                    {
                        if (byId.TryGetValue(appId, out var game) == true)
                        {
                            game.SetFamilyShared(result.Value);
                        }

                        if (done % 10 == 0 || done == pending.Count)
                        {
                            this._Main.StatusText = $"正在检测家庭共享（{done}/{pending.Count}）...";
                        }
                    });
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();

            try
            {
                await Task.WhenAll(tasks).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return; // 新一轮检测接管。
            }

            this._Dispatcher.TryEnqueue(() =>
            {
                this.ApplyFilter();
                var sharedCount = this._AllGames.Count(game => game.IsFamilyShared);
                this._Main.StatusText = failures > 0
                    ? $"共 {this._AllGames.Count} 个游戏（家庭共享检测完成，{failures} 个失败）"
                    : sharedCount > 0
                        ? $"共 {this._AllGames.Count} 个游戏，其中 {sharedCount} 个家庭共享"
                        : $"共 {this._AllGames.Count} 个游戏";
            });
        }

        /// <summary>用缓存回填徽章（零副作用，不做任何子进程探测）。</summary>
        private void ApplyFamilySharingFromCache()
        {
            var steamId = this._SteamService.GetSteamId();
            foreach (var game in this._AllGames)
            {
                if (this._FamilySharing.TryGetCached(steamId, game.Id, out var shared) == true)
                {
                    game.SetFamilyShared(shared);
                }
            }
        }

        /// <summary>扫描 Steam 库目录文件回填"已下载"标记（纯本地文件读取，毫秒级）。</summary>
        private void ApplyInstalledFromScan()
        {
            var installed = this._InstalledGames.ScanInstalledAppIds();
            foreach (var game in this._AllGames)
            {
                game.SetInstalled(installed.Contains(game.Id));
            }
        }

        /// <summary>
        /// 后台补齐中文名：磁盘缓存命中的即时回填，未命中的经商店接口限频逐个取
        /// （约 0.4s/个，154 个首启约 1 分钟，之后启动零请求）。名称即时刷新卡片；
        /// 全部完成后 ApplyFilter 重排序（中文名的排序位次与英文不同）。
        /// 刷新/手动添加会重启该过程（取消旧的）；搜索同时匹配中英文名。
        /// </summary>
        private async Task LocalizeNamesAsync()
        {
            this._LocalizeCancellation?.Cancel();
            this._LocalizeCancellation?.Dispose();
            this._LocalizeCancellation = new CancellationTokenSource();
            var cancellationToken = this._LocalizeCancellation.Token;

            var targets = this._AllGames
                .Where(game => game.Game.LocalizedName == null)
                .ToList();
            if (targets.Count == 0)
            {
                return;
            }

            // 纯缓存回填不发请求，不展示进度（避免每次启动刷状态栏）。
            var networkTargets = targets.Count(game => this._LocalizedNames.NeedsRequest(game.Id));
            if (networkTargets > 0)
            {
                this._Main.StatusText = $"正在获取中文名称（0/{networkTargets}）...";
            }

            var byId = targets.ToDictionary(game => game.Id);
            var doneNetwork = 0;
            var changed = 0;
            foreach (var game in targets)
            {
                bool needsNetwork = this._LocalizedNames.NeedsRequest(game.Id);
                string? name = null;
                try
                {
                    name = await this._LocalizedNames.GetAsync(game.Id, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return; // 新一轮回填接管。
                }

                if (needsNetwork == true)
                {
                    doneNetwork++;
                }

                if (string.IsNullOrEmpty(name) == false)
                {
                    changed++;
                }

                var done = doneNetwork;
                this._Dispatcher.TryEnqueue(() =>
                {
                    if (name != null && byId.TryGetValue(game.Id, out var target) == true)
                    {
                        target.SetLocalizedName(name);
                    }

                    if (needsNetwork == true && (done % 10 == 0 || done == networkTargets))
                    {
                        this._Main.StatusText = $"正在获取中文名称（{done}/{networkTargets}）...";
                    }
                });
            }

            this._Dispatcher.TryEnqueue(() =>
            {
                if (changed > 0)
                {
                    // 中文名就位后重排序并刷新过滤（搜索/排序都吃 Name）。
                    this.ApplyFilter();
                    this._Main.StatusText = $"已补充 {changed} 个游戏的中文名称";
                }
            });
        }

        /// <summary>双击/回车打开游戏 → 进入游戏详情（未保存更改确认走异步对话框）。</summary>
        [RelayCommand]
        private async Task OpenGameAsync(GameItemViewModel? item)
        {
            if (item == null)
            {
                return;
            }

            if (this._Main.CurrentView is GameDetailViewModel existing)
            {
                if (await existing.ConfirmLeaveAsync() == false)
                {
                    return;
                }

                existing.Dispose();
            }

            this._Main.CurrentView = new GameDetailViewModel(item.Id, item.Name, this._GameService, this._Main, this._Dialogs);
            this._Main.StatusText = $"已打开 {item.Name}（App ID {item.Id}）";
        }

        /// <summary>手动添加游戏（校验所有权后仅显示该游戏）。</summary>
        [RelayCommand]
        private async Task AddGameAsync()
        {
            var appId = await this._Dialogs.PromptForAppIdAsync();
            if (appId is not uint id)
            {
                return;
            }

            var info = this._GameService.AddSingleGame(id);
            if (info == null)
            {
                await this._Dialogs.ShowErrorAsync("Steam 成就管理器", "你不拥有该游戏，或 App ID 无效。");
                return;
            }

            this._AllGames = new List<GameItemViewModel>()
            {
                new(info, this._GameService),
            };
            this.SearchText = null;
            this.Ownership = OwnershipFilter.All;
            this.UsedFallback = false;
            // 重建分类会重置为“全部”，单游戏在其下必然可见（原版等价行为：
            // 强制勾选“游戏”复选框）。
            this.RebuildCategories();
            this.ApplyFilter();
            this._Main.StatusText = $"正在显示单个游戏（App ID {id}），刷新可恢复完整列表";
            this.ApplyFamilySharingFromCache();
            this.ApplyInstalledFromScan();
            _ = this.LocalizeNamesAsync();
        }
    }
}
