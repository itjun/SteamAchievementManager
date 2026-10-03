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
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SAM.Core.Services;
using SAM.WpfApp.Models;
using SAM.WpfApp.Views.Windows;

namespace SAM.WpfApp.ViewModels
{
    /// <summary>
    /// 游戏库 ViewModel。迁移自 SAM.Picker\GamePicker.cs：
    /// 搜索/类型过滤（RefreshGames 146-191）、刷新（OnRefresh）、手动添加
    /// （OnAddGame 474-507）、AppDataChanged 名称更新（81-97）。
    /// 类型过滤已从四复选框弹窗改为侧栏分类单选（Categories/SelectedCategory）。
    /// 双击打开游戏 → GameDetail 导航（Phase 6）。
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
        private readonly MainViewModel _Main;

        private List<GameItemViewModel> _AllGames = new();

        private CancellationTokenSource? _ProbeCancellation;

        [ObservableProperty]
        private ObservableCollection<GameItemViewModel> _Games = new();

        [ObservableProperty]
        private string? _SearchText;

        /// <summary>所有权筛选（自购/家庭共享），与侧栏类型分类正交。</summary>
        [ObservableProperty]
        private OwnershipFilter _Ownership = OwnershipFilter.All;

        /// <summary>安装状态筛选（已下载/未下载），与所有权/类型筛选正交。</summary>
        [ObservableProperty]
        private InstallFilter _Install = InstallFilter.All;

        /// <summary>所有权 ComboBox 选项。</summary>
        public IReadOnlyList<OwnershipChoice> OwnershipChoices { get; } = new[]
        {
            new OwnershipChoice("全部", OwnershipFilter.All),
            new OwnershipChoice("自购", OwnershipFilter.Owned),
            new OwnershipChoice("家庭共享", OwnershipFilter.FamilyShared),
        };

        /// <summary>安装状态 ComboBox 选项。</summary>
        public IReadOnlyList<InstallChoice> InstallChoices { get; } = new[]
        {
            new InstallChoice("全部", InstallFilter.All),
            new InstallChoice("已下载", InstallFilter.Installed),
            new InstallChoice("未下载", InstallFilter.NotInstalled),
        };

        /// <summary>侧栏分类列表（"全部" 恒有；其余类型仅在本库存在时出现）。</summary>
        public IReadOnlyList<GameCategory> Categories { get; private set; } = Array.Empty<GameCategory>();

        /// <summary>加载/刷新/手动添加后触发，MainWindow 据此重建侧栏菜单。</summary>
        public event EventHandler? CategoriesChanged;

        [ObservableProperty]
        private GameCategory? _SelectedCategory;

        [ObservableProperty]
        private bool _IsLoading;

        /// <summary>games.xml 下载失败，已回退默认列表（原版静默回退 Spacewar）。</summary>
        [ObservableProperty]
        private bool _UsedFallback;

        [ObservableProperty]
        private string? _EmptyMessage;

        [ObservableProperty]
        private bool _ShowEmpty;

        public GameLibraryViewModel(SteamService steamService, GameService gameService, FamilySharingService familySharing, InstalledGamesService installedGames, MainViewModel main)
        {
            this._SteamService = steamService;
            this._GameService = gameService;
            this._FamilySharing = familySharing;
            this._InstalledGames = installedGames;
            this._Main = main;

            // 回调泵在 UI 线程触发 RunCallbacks → 事件在 UI 线程，可直接操作集合。
            this._SteamService.AppDataChanged += this.OnAppDataChanged;

            _ = this.LoadAsync();
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

        partial void OnOwnershipChanged(OwnershipFilter value) => this.ApplyFilter();

        partial void OnInstallChanged(InstallFilter value) => this.ApplyFilter();

        /// <summary>
        /// 按当前库重算分类（含数量），并重置选中为“全部”：数据集变了从全集看起，
        /// 也与 MainWindow 重建菜单后 Navigate 自动选中首项（全部）的高亮一致。
        /// 四个固定类型无论数量多少恒显示（用户要求"直接全部显示"——侧栏项
        /// 随刷新忽隐忽现会显得混乱）；"其他"归拢未知类型，仅有数据时出现。
        /// 最后通知 MainWindow 重建侧栏。
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
        /// 自动更新排队（2026-10-04 实测引发过一轮批量下载）——启动时只读缓存
        /// 回填徽章（ApplyFamilySharingFromCache），子进程探测只在你点了按钮后进行。
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
                    await App.Current.Dispatcher.InvokeAsync(() =>
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

            await App.Current.Dispatcher.InvokeAsync(() =>
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

        /// <summary>双击/回车打开游戏 → 进入游戏详情（Phase 6 统一导航；不再 Process.Start）。</summary>
        [RelayCommand]
        private void OpenGame(GameItemViewModel? item)
        {
            if (item == null)
            {
                return;
            }

            if (this._Main.CurrentView is GameDetailViewModel existing)
            {
                if (existing.TryConfirmLeave() == false)
                {
                    return;
                }
                existing.Dispose();
            }

            this._Main.CurrentView = new GameDetailViewModel(item.Id, item.Name, this._GameService, this._Main);
            this._Main.StatusText = $"已打开 {item.Name}（App ID {item.Id}）";
        }

        /// <summary>手动添加游戏（原 OnAddGame 474-507：校验所有权后仅显示该游戏）。</summary>
        [RelayCommand]
        private void AddGame()
        {
            var dialog = new AddGameWindow()
            {
                Owner = Application.Current?.MainWindow,
            };

            if (dialog.ShowDialog() != true || dialog.AppId is not uint appId)
            {
                return;
            }

            var info = this._GameService.AddSingleGame(appId);
            if (info == null)
            {
                MessageBox.Show(
                    "你不拥有该游戏，或 App ID 无效。",
                    "Steam 成就管理器",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
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
            this._Main.StatusText = $"正在显示单个游戏（App ID {appId}），刷新可恢复完整列表";
            this.ApplyFamilySharingFromCache();
            this.ApplyInstalledFromScan();
        }
    }
}
