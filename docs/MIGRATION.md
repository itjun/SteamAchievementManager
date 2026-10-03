# SAM WPF 重构迁移文档

> 本文档是 SAM（Steam Achievement Manager）从 WinForms / .NET Framework 4.8 重构为
> WPF / .NET 8 的分析与迁移跟踪文档。分析基于 master 分支 `de8b710`（2026-10-03）。
> 本轮为 **Phase 1**：仅建立新项目骨架并保证全解决方案可编译，未迁移任何业务逻辑，
> 未删除/未修改任何旧 WinForms 源代码。

---

## 1. 目标架构

```
SAM.Wpf      net8.0-windows, WPF, WPF-UI, CommunityToolkit.Mvvm   （视图 + ViewModel）
    ↓
SAM.Core     net8.0-windows, 无 UI 依赖                            （业务逻辑 / 服务 / 模型）
    ↓
SAM.API      net48;net8.0-windows（多目标，源码零改动）             （Steam 原生互操作）

SAM.Picker / SAM.Game（net48 WinForms）—— 迁移期间原样保留，功能对齐后移除
```

硬约束：

- **全程 x86**。steamclient.dll 是 32 位、ThisCall vtable 互操作
  （`SAM.API\Steam.cs:123` 加载 `<Steam>\steamclient.dll`）。所有项目的
  `Platforms=x86` 不可改。
- SAM.Core 禁止引用 WPF / WinForms 类型。
- 不改变 Steam API 核心行为（初始化方式、回调机制、保存序列）。

---

## 2. 现状分析（SAM 7.0，2024 重写版）

注意：本仓库是 SAM 7.0，与 SAM 6.x 有本质差异——**没有**导出模式/子进程、
没有 SteamID `0x0110000100000000` 转换、没有 HTTP schema API。

### 2.1 SAM.Picker（GamePicker.exe）职责

| 职责 | 位置 | 说明 |
|---|---|---|
| 游戏列表 | `GamePicker.cs:99-131` | 下载 `https://gib.me/sam/games.xml`，XPath 解析出 `(uint id, string type)`；**不是** Steam API 枚举 |
| 所有权过滤 | `GamePicker.cs:397-417` | `SteamApps008.IsSubscribedApp(id)`；名称取 `SteamApps001.GetAppData(id,"name")` |
| 分类 | games.xml `type` 属性 | normal / demo / mod / junk（非 EAppType）；未知类型恒显示 |
| 搜索/过滤 | `GamePicker.cs:146-191` | 名称子串（OrdinalIgnoreCase）+ 四类开关；按名称排序 |
| 图标 URL | `GamePicker.cs:338-366` | 三级回退：`small_capsule/{当前语言}` → `small_capsule/english`（cloudflare 域名）→ `logo`（cdn 域名 .jpg） |
| 图标下载 | `GamePicker.cs:252-336` | BackgroundWorker 单飞 + ConcurrentQueue；仅下载可见项（懒加载）；仅内存缓存、会话内不重试 |
| 启动 Manager | `GamePicker.cs:455` | `Process.Start("SAM.Game.exe", appId)`——依赖两 exe 同目录 |
| 手动添加 | `GamePicker.cs:474-507` | 输入 appid，校验所有权后**清空列表**只显示该游戏 |
| 回调泵 | `GamePicker.cs:431-436` | 100ms WinForms Timer → `Client.RunCallbacks`（AppDataChanged 1001 → 更新名称/图标） |
| 设置持久化 | 无 | 无任何设置存储，过滤器状态每次运行重置 |
| 单实例 | 无 | 无互斥体，可多开 |

### 2.2 SAM.Game（Manager.exe）职责

| 职责 | 位置 | 说明 |
|---|---|---|
| 启动参数 | `Program.cs:34-50` | 恰好 1 个参数 = appid；无参数则反向启动 Picker |
| Schema 加载 | `Manager.cs:206-365` | 读本地 `{Steam}\appcache\stats\UserGameStatsSchema_{id}.bin`（二进制 VDF，`KeyValue.cs` 解析）；兼容新（type 字符串）/旧（type_int）格式 |
| 本地化 | `Manager.cs:180-204` | `GetLocalizedString`：当前语言 → english → 节点自身值 → 默认值 |
| 成就读取 | `Manager.cs:441-538` | 逐项 `GetAchievementAndUnlockTime`；`#` 开头的未本地化名字显示 Id |
| 成就图标 | `Manager.cs:149-172` | `https://cdn.steamstatic.com/steamcommunity/public/images/apps/{id}/{icon}`；IconLocked 空则回退 IconNormal；失败用空白位图 |
| 统计读取 | `Manager.cs:540-583` | 按定义 `GetStatValue` int/float；Extra 列 = StatFlags（IncrementOnly/Protected/UnknownPermission） |
| **保存流程** | `Manager.cs:605-794` | 见 §3 行为保真清单 |
| 重置 | `Manager.cs:829-862` | 三重确认后 `ResetAllStats(achievementsToo)` |
| 受保护项 | `Manager.cs:864-891` | 成就 `Permission&3` / 统计 `Permission&2` 不可改 |
| 回调泵 | `Manager.cs:713-718` | 100ms Timer → `RunCallbacks`（UserStatsReceived 1101 → 加载 schema/成就/统计） |

### 2.3 SAM.API

纯互操作层，零 UI 依赖：`SteamClient018` / `SteamUser012` / `SteamUserStats013` /
`SteamUtils005` / `SteamApps001` / `SteamApps008` 的 ThisCall vtable 封装，
回调分发（1101 UserStatsReceived、1001 AppDataChanged），注册表读 Steam 安装路径。
唯一 Windows API 依赖是 `Microsoft.Win32.Registry.GetValue`。

---

## 3. 行为保真清单（重构中不可改变）

1. **保存序列**：仅由「提交更改」按钮触发 →
   StoreAchievements（仅变更项，逐个 Set/ClearAchievement，任一失败立即中止）
   → StoreStatistics（仅 IsModified 项，任一失败立即中止）
   → StoreStats()（失败中止）
   → **无论成败最后都 RefreshStats()**（重新 RequestUserStats 并整体刷新）。
2. **统计值无钳制**：schema 的 min/max/maxchange/default 被解析但不使用；
   仅校验可解析性与受保护位。
3. **受保护判定**：成就 `Permission & 3`、统计 `Permission & 2`。
4. **回调泵**：100ms UI 线程泵 `RunCallbacks` 是一切回调（含数据加载）的驱动源；
   WPF 中以 DispatcherTimer 等价替代，保持 UI 线程单线程模型。
5. **Schema 来源**：本地 VDF bin 文件（要求 Steam 曾同步过该游戏的 stats），
   不是 Web API。
6. **游戏列表来源**：gib.me 的 games.xml + IsSubscribedApp 过滤；
   下载失败回退 Spacewar(480)。
7. **图标 URL 规则**与回退逻辑（§2.1/§2.2）。
8. **Client 初始化**：`Initialize(appId)` 设置 `SteamAppId` 环境变量，
   Picker 用 0，Game 用目标游戏 id；从 Steam 目录运行时拒绝启动。
9. 过滤语义：搜索子串匹配（OrdinalIgnoreCase，Picker 按名称、Game 按名称+描述）；
   成就 Locked/Unlocked 筛选互斥；未知游戏类型恒显示。

已知瑕疵（迁移时顺手修复，不改变行为）：Picker 状态栏文本跨线程直写
（`GamePicker.cs:101,126`）、logo 队列 HashSet 跨线程竞态（`:256` vs `:389`）、
`OnDownloadList` 取消时 `e.Error` 空引用（`:138`）、统计值解析用 CurrentCulture
（`IntStatInfo.cs:35` / `FloatStatInfo.cs:35`——新 UI 明确改用 InvariantCulture 或受控输入）。

---

## 4. WinForms 耦合点清单

| 耦合点 | 位置 | 迁移策略 |
|---|---|---|
| `GameInfo.Item(ListViewItem)/ImageIndex/ImageUrl` | `SAM.Picker\GameInfo.cs:34-44` | 模型只留 Id/Type/Name；图标缓存移入服务层 |
| `AchievementInfo.Item/ImageIndex` | `SAM.Game\Stats\AchievementInfo.cs:38-46` | IsAchieved 暂存于模型本身；图标由 VM 缓存 |
| 成就状态真值在 `ListViewItem.Checked` | `Manager.cs:613-623` | 改为模型属性 + 变更标记（HasChanges）驱动保存按钮 |
| 图标缓存寄生 ImageList | `GamePicker.cs:368-395` / `Manager.cs:585-603` | 以 URL/文件名为键的字典缓存 |
| `RefreshGames`/`GetAchievements` 直读控件状态 | `GamePicker.cs:146-155` / `Manager.cs:443-454` | 过滤条件上移 ViewModel 属性 |
| 可见性裁剪用 `ListViewItem.Bounds` | `GamePicker.cs:322` | WPF 虚拟化 + 异步加载策略 |
| 100ms WinForms Timer 泵回调 | 两处 `OnTimer` | DispatcherTimer（保留 UI 线程单线程模型） |
| DataGridView 编辑管线 | `Manager.cs:64-84, 796-827` | 现代编辑控件 + 受保护校验保留异常语义 |
| MessageBox ×15 处 | Manager.cs / Program.cs | 集中为 ViewModel → View 的对话服务（中文文案） |
| `MyListView` 滚动检测 | `SAM.Picker\MyListView.cs` | **死代码（Scroll 无订阅者），不迁移** |
| `DoubleBufferedListView` | `SAM.Game\DoubleBufferedListView.cs` | WPF 内建，不迁移 |

## 5. 迁移映射

| 旧代码 | → ViewModel | → SAM.Core | → View |
|---|---|---|---|
| GamePicker.cs 列表/所有权/分类 | GameLibraryViewModel | GameService + Models.GameInfo | GameLibraryView |
| GamePicker.cs 搜索/过滤/排序 | GameLibraryViewModel | 过滤谓词（纯函数） | GameLibraryView |
| GamePicker.cs 图标下载/懒加载 | GameLibraryViewModel | GameService（LogoCache，修竞态） | GameLibraryView |
| GamePicker.cs OnAddGame | GameLibraryViewModel | GameService.AddSingleGame | GameLibraryView |
| GamePicker.cs 启动 SAM.Game | MainViewModel 导航 | —（单窗口内切换 GameDetail） | — |
| GamePicker.cs GetGameImageUrl | — | GameService.GetImageUrl | — |
| Manager.cs Program.cs 守卫/初始化 | App.xaml.cs | SteamService.Initialize | — |
| Manager.cs 回调泵 | — | SteamService（DispatcherTimer） | — |
| Manager.cs LoadUserGameStatsSchema | GameDetailViewModel | AchievementService.LoadSchema | — |
| SAM.Game KeyValue*.cs / StreamHelpers.cs | — | SAM.Core（VDF 解析器原样迁入） | — |
| Manager.cs GetAchievements/图标 | AchievementsViewModel | AchievementService + 模型 | AchievementsView |
| Manager.cs StoreAchievements | AchievementsViewModel | AchievementService.Store | 底部 Action Bar |
| Manager.cs GetStatistics/编辑/重置 | StatisticsViewModel | StatisticsService + 统计模型 | StatisticsView |
| Manager.cs OnTimer | — | SteamService | — |
| SAM.API 全部 | — | 原样复用（多目标编译） | — |
| MyListView.cs / DoubleBufferedListView.cs | — | 不迁移 | — |

新 UI 界面文案使用**简体中文**（已确认）；游戏名、成就名等 Steam 数据仍按原文显示。

---

## 6. 已知架构约束（Phase 4/6 已解决）

**ISteamUserStats 绑定进程 SteamAppId 上下文**——已实验证实（appId=0 请求返回
Result=8；同进程重建 Client 抛 appID mismatch）。解决方案：**自宿主统计子进程**
（`SAM.Wpf.exe --stats-worker={appId}` + JSON-lines stdio 协议），见 §Phase 4-7 记录。

## 7. Phase 跟踪

| Phase | 内容 | 状态 |
|---|---|---|
| 1 | SAM.Core / SAM.Wpf 骨架 + SAM.API 多目标 + 全解决方案编译 | ✅ 完成 |
| 2 | FluentWindow 主窗口、NavigationView（游戏/设置）、深浅主题、PerMonitorV2 DPI | ✅ 完成 |
| 3 | 迁移游戏库（games.xml 下载、所有权过滤、搜索/分类、图标、SteamService 接入） | ✅ 完成 |
| 4 | 迁移成就（schema 读取、显示、锁定/解锁、图标、保存） | ✅ 完成 |
| 5 | 迁移统计（读取、编辑、保存、重置） | ✅ 完成 |
| 6 | Picker + Manager 合并为统一导航（含未保存变更确认） | ✅ 完成 |
| 7 | 功能对比测试 → 移除旧 WinForms 项目 | ✅ 完成（2026-10-03） |

### Phase 4-7 实现记录（2026-10-03）

**关键架构决策（实验驱动）**：ISteamUserStats 绑定进程的 SteamAppId 上下文。
实验证实：appId=0 上下文请求统计返回 `GameId=0 Result=8`；同进程销毁重建 Client
并改 env `SteamAppId` 后 `Initialize(440)` 抛 `appID mismatch`（steamclient 的
app 绑定在进程级）。因此新客户端采用**自宿主统计子进程**：

- 主进程保持 appId=0（Picker 上下文：库/图标/AppData 查询）。
- 打开游戏详情时以 `--stats-worker={appId}` 启动**自身副本**（隐藏、重定向 stdio），
  JSON-lines 协议（`get` / `store` / `resetAll`），离开详情时 Dispose（关 stdin 自然退出）。
- 对用户呈现为单一应用；这正是旧版按游戏 spawn SAM.Game.exe 的等价物。

**SAM.Core 新增**：`Schema/`（KeyValue VDF 解析器逐字迁移）、`GameStats/`
（协议 DTO + StatsWorker 子进程逻辑 + StatsClient 父进程客户端）、
SteamService 扩展 UserStats 接口与 UserStatsReceived 事件、
AchievementService/StatisticsService（图标 URL/过滤谓词/受保护判定/Extra/Invariant 解析）。

**SAM.Wpf 新增**：GameDetailViewModel（成就/统计双区、搜索/状态筛选/批量操作、
变更跟踪、保存/重置命令、未保存离开确认）；AchievementItemViewModel（Pending 编辑态 +
状态图标懒加载）；StatisticRowViewModel（行内编辑 + 校验 + 受保护禁用）；
GameDetailView（成就卡片 + 统计表格 + 底部 Action Bar：重置/变更计数/保存更改——
仅在有修改时启用）；游戏库↔详情在 MainViewModel.CurrentView 间切换（单窗口统一导航）。

**保存语义保真**：仅提交差异项 → 逐项 SetAchievement/SetStatValue（任一失败立即中止）
→ StoreStats → 无论成败整体刷新；重置保留三重确认；受保护成就 Permission&3、
统计 Permission&2（新 UI 以禁用交互替代弹窗拦截，效果等价）。

**真机验证证据**（Steam 运行中，2026-10-03）：
- 库：真实列表（Dota 2/TF2/L4D2/CS2/Spacewar）+ 图标；搜索 "dota" 过滤至 1 行，状态栏「显示 1 / 5」。
- 详情（TF2 440）：520/521 成就加载（名称/描述/解锁时间/图标），统计页数值（TF_PLAYER_KILLS 96426.0 等），
  统计编辑开关默认关。
- 变更跟踪（UI Automation + 截图双重验证）：勾选第一项成就 → 「有 1 项未保存的修改」+
  保存按钮强调色启用；还原 → 「没有未保存的修改」+ 禁用。
- 未实际执行保存/重置到 Steam 服务器（避免修改真实账号数据；该路径为旧版逐字移植 + worker 镜像旧调用序列）。

**功能对比结论**：新版完整覆盖旧版全部功能（列表/搜索/筛选/刷新/图标/手动添加/
成就读写/批量/保存/受保护/统计读写/重置），并新增主题切换、设置持久化、未保存确认。
据此执行了移除：SAM.Picker、SAM.Game 已从解决方案删除并移除目录（git 历史可恢复）。
SAM.API 保留（多目标 net48;net8.0-windows，net48 目标暂无消费者，保留以便需要）。

### WPF-UI 4.3 API 备忘（实测）

### Phase 2 实现记录（2026-10-03）

- 主窗口：`FluentWindow` + Mica 背景 + 自定义 TitleBar（图标 + 标题 + 最小化/最大化/关闭），
  圆角窗口（Win11），PerMonitorV2 高 DPI（`SAM.Wpf\app.manifest`）。
- 导航：NavigationView 左侧窗格（默认展开 220px）+ 页脚「设置」；零 DI 的
  `PageService : INavigationViewPageProvider` 提供页面实例（缓存复用）。
- 主题：`ThemeService`（深色/浅色/跟随系统，`ApplicationThemeManager.Apply` +
  `SystemThemeWatcher`），Accent 用系统强调色；选择持久化到
  `%LOCALAPPDATA%\SAM.Wpf\settings.json`（`SettingsService`）。
- MVVM：CommunityToolkit.Mvvm（ObservableObject / ObservableProperty），
  SettingsViewModel 驱动主题切换；调试辅助 `--theme=dark|light|system`。
- 根命名空间为 `SAM.WpfApp`（程序集名仍为 SAM.Wpf）：`SAM.Wpf` 会在 SAM 作用域内
  把 `Wpf.Ui.*` 错误解析为 `SAM.Wpf.Ui.*`（命名空间遮蔽，XAML 生成代码无法规避）。

### Phase 3 实现记录（2026-10-03）

- **SAM.Core.Services.SteamService**：Client 生命周期 + Steam 目录守卫 + 初始化失败分类
  （`SteamInitFailure` 枚举，上层映射中文文案）+ `AppDataChanged` 事件 + `RunCallbacks`
  （由 SAM.Wpf 的 100ms DispatcherTimer 在 UI 线程驱动，等价旧版 WinForms Timer）。
- **SAM.Core.Services.GameService**：games.xml 下载/解析（XPath 逐字迁移）、
  `IsSubscribedApp` 所有权过滤 + `GetAppData` 取名、Spacewar 回退（`UsedFallback` 标记）、
  过滤谓词（normal/demo/mod/junk，未知类型恒显示）、图标 URL 三级回退、
  图标单飞下载（URL 去重 + 会话内失败不重试，修复旧版跨线程 HashSet 竞态）。
  内部全部 `ConfigureAwait(false)`，原生调用在线程池（等价旧版 BackgroundWorker）。
- **SAM.Wpf**：App 启动接 Steam（失败中文对话框 + 退出）；MainWindow 底部状态栏（MainViewModel）；
  GameLibraryViewModel（搜索 200ms 防抖、四类筛选、刷新、手动添加对话框、双击/回车打开占位）；
  GameItemViewModel 图标**绑定求值即懒加载**（WPF 虚拟化天然等价旧版"仅下载可见项"）；
  列表为 72px 紧凑行（146x52 胶囊图 + 名称 + "App ID {id} · 类型" + chevron）。
- **真机验证**（Steam 运行中）：列表加载真实库（Dota 2/TF2/L4D2/CS2/Spacewar），
  图标懒加载出图；搜索 "dota" 过滤至 1 行且状态栏显示「显示 1 / 5 个游戏」；
  深浅主题均正常。双击游戏暂时弹「Phase 4 提供」提示。
- 旧 SAM.Picker/SAM.Game 未动，仍可独立运行。

### WPF-UI 4.3 API 备忘（实测）

- 页面提供器接口是 `Wpf.Ui.Abstractions.INavigationViewPageProvider`（单方法 `GetPage(Type)`），
  挂到 NavigationView 用 **`SetPageProviderService`**（不是旧版的 SetPageService）。
- `FluentWindow` **不实现** `INavigationWindow`（该接口供应用窗口自行实现）。
- `ui:TitleBar` 必须作为**窗口 Grid 的直接子元素**渲染；`NavigationView.TitleBar="{Binding ElementName=TitleBar}"` 仅做布局关联（Gallery 同款结构，声明在 NavigationView 之后以叠放其上）。
- `ApplicationThemeManager.GetSystemTheme()` 返回 `SystemTheme` 枚举，需自行映射到 `ApplicationTheme`。
- NavigationView **没有公开的程序化选中 API**：`SelectedItem` 无 public setter（CS0272），
  `NavigationViewItem` 也**没有** `IsSelected`（DLL 里能 grep 到的 IsSelected 属于其他控件）。
  程序化高亮只能靠 `Navigate(pageType)`——它按页面类型自动选中首个匹配菜单项。
- 动态菜单用 `MenuItemsSource`（可赋 `ObservableCollection<object>`，元素为
  `NavigationViewItem`）与 XAML 的 `FooterMenuItems` 共存；重建后选中高亮会丢失，
  需再调一次 `Navigate` 恢复。
- 菜单项点击时序：`PreviewMouseLeftButtonDown`（隧道）先于 WPF-UI 内部导航处理，
  可在其中改 ViewModel 状态（如筛选条件），点击本身仍会自动导航到 TargetPageType。
- NavigationViewItem 不暴露 UIA Invoke/SelectionItem 模式，自动化测试用
  BoundingRectangle 中心坐标 + `SetCursorPos`/`mouse_event` 点击（行高 ~36px 足够可靠）。

### 侧栏类型分类（2026-10-03 追加）

- 原筛选弹窗（四复选框）移除，改为 NavigationView 侧栏分类单选：
  `GameLibraryViewModel.Categories`（按库自动出现，"全部"恒有，未知类型归"其他"）
  + `SelectedCategory` 驱动 `GameService.MatchesFilter(info, search, categoryKey)`。
- MainWindow 订阅 `CategoriesChanged` 重建 `MenuItemsSource` 并 `Navigate` 恢复高亮；
  重建后统一重置"全部"（与 Navigate 自动选中首项一致，避免筛选/高亮错位）。
- 实测：155 库 → 全部 (155)/游戏 (154)/杂项 (1)，试玩/Mod 无数据自动隐藏；
  点击杂项 → 仅 Spacewar（显示 1/155）。

### 家庭共享判别（2026-10-04 重做定案，推翻 10-03 的临时结论）

- **功能**：列表行橙色"家庭共享"徽章 + 顶部所有权 ComboBox（全部/自购/家庭共享）。
- **原生接口定案**（探测工具 `--probe-familysharing=ids`（主进程诊断）与
  `--probe-appctx=<appId>`（按游戏上下文，FamilySharingService 解析其输出））：
  - steamclient ISteamApps008 客户端 vtable **恰好 28 槽**（0..27），无 2023+ 追加；
  - `IsSubscribedFromFamilySharing` 是**无参的"当前进程 SteamAppId 上下文"语义**：
    主进程（appId=0）带 appId 参数调用不会崩但**恒返回假值**（10-03 的"全自购"
    结论因此作废）；在以目标 appId 初始化的子进程里无参调用才可靠；
  - `GetAppOwner` 槽位带参/无参均 0xC0000005，不可用；
  - `GetEarliestPurchaseUnixTime(appId)` 可用，但**家庭共享游戏返回借出方的
    购买时间**（==0 兜底判别同样无效）。
- **架构**（`SAM.Core/Services/FamilySharingService.cs`）：每游戏一个隐藏子进程
  （自身副本 `--probe-appctx=<id>`，与 stats-worker 同机制）探测，并发 3、单探测
  超时 20s；结果按 SteamID+appId 缓存 `%LOCALAPPDATA%\SAM.Wpf\familysharing.json`
  （7 天过期）。加载流程：列表先出 → 缓存回填徽章 → 未命中后台探测（徽章随
  INPC 即时出现，不重建列表）→ 完成后 ApplyFilter 一次让所有权筛选生效。
- **本机实测**（账号 76561198421450282）：155 个 App = **92 家庭共享 + 63 自购**
  （鬼谷八荒/7 Billion Humans/嗜血印等为共享；TF2/古剑奇谭三等为自购）；
  首次全量探测约 1 分钟，二次启动缓存秒读（徽章立即恢复）。
- **探测改为仅手动（2026-10-04 用户决定）**：探测子进程会让 Steam 客户端把该游戏
  短暂标记为"运行中"（content_log 的 `state changed : ..., App Running`），实测
  00:51 批量探测 155 个后，00:58 Steam 为**已安装**的共享游戏排队可选更新，
  引发多 GB 下载（嗜血印 992300 更新 2 万文件）。因此：
  - 启动/刷新/手动添加只**读缓存**回填徽章（ApplyFamilySharingFromCache，零副作用）；
  - 页头新增"检测共享"按钮（DetectFamilySharingCommand）= 全量强制重探并覆盖缓存，
    tooltip 注明可能触发已安装游戏的更新检查；
  - 缓存有效期放宽到 365 天（手动模式下过期会让徽章悄悄消失，仅作自愈上限）。
- 排查教训：WPF-UI/原生混合调试时，`dotnet build | grep "错误"` 会被 GBK 乱码
  掩盖失败（管道内中文变 mojibake）——构建判定用 exit code 或 grep "error CS"；

### 已下载（本地安装）标记与筛选（2026-10-04）

- **数据源**（`SAM.Core/Services/InstalledGamesService.cs`，纯文件读取、零 steamclient
  调用、零副作用）：两源并集——
  1. `steamapps\libraryfolders.vdf` 各库 "apps" 段（覆盖多盘库，但 Steam **懒更新**，
     刚装完的游戏可能没写进去）；
  2. 各库 `appmanifest_*.acf` 文件名即 AppId + StateFlags 含位 4（Fully Installed）
     ——最及时的权威信号（实测 992300 嗜血印只在源 2 出现）。
- **UI**：第二行新增 "已下载" 弱徽标（CheckmarkCircle24 + 主题色弱芯片，与家庭共享
  橙色状态章并存互不干扰）；搜索行右侧第二个 ComboBox（全部/已下载/未下载），
  与所有权/类型筛选正交；空态提示 "没有已下载到本机的游戏"。
- 实测：已下载 → 6/155（嗜血印/黑魂3/Detroit/P5R/只狼/古墓丽影），未下载 → 149；
  刷新/手动添加后重扫（毫秒级）。

### 侧栏稳定化与窗格头部修复（2026-10-04）

- **分类恒显**：四个固定类型（游戏/试玩/Mod/杂项）无论数量恒在侧栏（0 也带计数显示），
  消除"分类随刷新忽隐忽现"；"其他"仅在有未知类型数据时出现。
- **窗格头部布局修复**：隐藏汉堡按钮后，TitleBar 左置的应用名/图标由
  NavigationView.TitleBar 机制叠印到窗格顶部，与首个菜单项视觉混乱。修法：
  TitleBar 清空（Title=""，无 Icon，仅右侧窗口按钮），窗格头部改由
  `NavigationView.PaneHeader` 显式渲染 **logo 在前 + 应用名同行**（ImageIcon 22px
  + TextBlock，Margin 16,12）——自带占位高度，菜单项自然下移。
- 验证手段：UIA 文本元素坐标（标题 y≈15/h19、首项 y≈74、40px 节奏）+
  像素文本带拓扑（%TEMP%\sam-topology.ps1，暗色主题换亮文本阈值）。
  坑：截图必须先 `SetForegroundWindow`，否则捕获到遮挡/合成残影（Mica 尤甚，
  曾得到整片 R=44 G=15 B=15 的伪截图）。

### 列表行标识信息层级重做（2026-10-04，frontend-design 评审）

- **家庭共享状态章**（行内唯一强调色）：PeopleCommunity24 图标 + 12px SemiBold
  文字 + 同色 1px 描边 + 悬停 tooltip（解释语义）；自购行无标记（默认态降噪）。
- **配色为主题感知画刷**：App.xaml 放浅色基线，ThemeService 订阅
  `ApplicationThemeManager.Changed`（含 SystemThemeWatcher 路径）改写
  `FamilySharedText/Background/BorderBrush`；文本对比度 WCAG 校准
  （浅 #9A5B14≈4.7:1 / 深 #E8A33D≈4.6:1），像素级验证两主题均生效、旧色零残留。
- **第二行信息结构化**：中点 meta 串（"App ID 440 · 游戏"）拆为
  [类型徽标]（ControlFillColorSecondaryBrush 弱芯片，TypeLabel）+ "App ID nnn"
  （TextFillColorTertiary 三级文本），GameItemViewModel.Subtitle 收窄为纯 App ID。

## 8. 环境说明

- .NET SDK 10.0.401（可编译 net8.0；本机装有 .NET 8 桌面运行时 8.0.10）
- VS 2026 Community MSBuild：`C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe`
- 构建命令：`MSBuild SAM.sln /restore /p:Configuration=Debug /p:Platform=x86`
- 旧项目输出仍为 `bin\`（net48）；新项目输出在各自 `bin\x86\...`（net8），互不干扰
- WPF-UI 4.3.0 + CommunityToolkit.Mvvm 8.4.2（NuGet 走 nuget.azure.cn 镜像）
- 已知残留：`bin\Krypton.Toolkit.dll` 为旧实验遗留（未跟踪、无引用），可随时删除
