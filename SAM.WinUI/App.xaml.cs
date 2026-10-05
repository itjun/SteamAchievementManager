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

using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SAM.Core.Services;
using SAM.WinUIApp.Models;
using SAM.WinUIApp.Services;
using SAM.WinUIApp.ViewModels;

namespace SAM.WinUIApp
{
    /// <summary>
    /// WinUI 3 应用入口：手工组装（无 DI 容器，对齐 WPF 版结构）。
    /// 统计 worker 与家庭共享探测子进程已拆分至 SAM.Worker.exe
    /// （WinUI 入口经 WASDK 引导后才有控制权，不适合作为子进程宿主）。
    /// </summary>
    public partial class App : Application
    {
        private SettingsService _SettingsService = null!;
        private ThemeService _ThemeService = null!;
        private DialogService _DialogService = null!;
        private UpdateService _UpdateService = null!;
        private SteamService _SteamService = null!;
        private GameService? _GameService;
        private MainViewModel _MainViewModel = null!;

        private DispatcherQueueTimer? _CallbackTimer;
        private bool _CleanedUp;

        public App()
        {
            this.InitializeComponent();
            this.UnhandledException += this.OnUnhandledException;
            AppDomain.CurrentDomain.ProcessExit += this.OnProcessExit;
        }

        /// <summary>主窗口单例引用（对话框 XamlRoot 等处使用）。</summary>
        public static Window? MainHost { get; private set; }

        protected override void OnLaunched(LaunchActivatedEventArgs args)
        {
            this._SettingsService = new SettingsService();
            this._ThemeService = new ThemeService();
            this._DialogService = new DialogService();
            this._UpdateService = new UpdateService();
            this._MainViewModel = new MainViewModel();

            var theme = this._SettingsService.Settings.Theme;

            // 调试辅助：--theme=dark|light|system 覆盖已保存的主题（不写回设置文件）。
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase) == true &&
                    Enum.TryParse(arg.Substring(8), true, out ThemeMode parsed) == true)
                {
                    theme = parsed;
                }
            }

            var mainWindow = new MainWindow(
                this._MainViewModel, this._SettingsService, this._ThemeService, this._UpdateService);
            MainHost = mainWindow;
            this._ThemeService.Apply(theme, mainWindow);
            mainWindow.Activate();

            // 更新检查放在 Steam 初始化前启动：初始化失败的早退路径也能弹出
            // 强制更新（与错误对话框的冲突由 ShowWithRetryAsync 兜底）。
            _ = this.RunStartupUpdateCheckAsync();

            // 初始化 Steam（Picker 模式 appId=0）；失败不弹框、不退出——应用照常
            // 启动，页面内提示原因（未安装/未运行等），安装并登录后可点"重试"。
            this._SteamService = new SteamService();
            this._MainViewModel.SteamRetryRequested += () => this.InitializeSteam(mainWindow);
            this.InitializeSteam(mainWindow);
        }

        /// <summary>
        /// Steam 初始化 + 游戏库组装（启动与页面"重试"共用）。重复调用安全：
        /// SteamService.Initialize 幂等，回调泵只创建一次。
        /// </summary>
        private void InitializeSteam(MainWindow mainWindow)
        {
            var initResult = this._SteamService.Initialize(0);
            if (initResult.Success == false)
            {
                var (title, detail) = DescribeInitFailure(initResult);
                this._MainViewModel.MarkSteamUnavailable(title, detail);
                return;
            }

            this._MainViewModel.ClearSteamUnavailable();

            // Steam 回调泵：等价 WPF 版 100ms DispatcherTimer。
            if (this._CallbackTimer == null)
            {
                var timer = mainWindow.DispatcherQueue.CreateTimer();
                timer.Interval = TimeSpan.FromMilliseconds(100);
                timer.Tick += (_, _) => this._SteamService.RunCallbacks();
                timer.Start();
                this._CallbackTimer = timer;
            }

            this._GameService = new GameService(this._SteamService);

            // 游戏库组装：worker/探测子进程 = SAM.Worker.exe；缓存目录 %LOCALAPPDATA%\SAM
            //（SettingsService 已一次性迁移 SAM.Wpf 的 familysharing.json）。
            var workerPath = System.IO.Path.Combine(AppContext.BaseDirectory, "SAM.Worker.exe");
            var dataDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SAM");
            var familySharing = new FamilySharingService(workerPath, dataDirectory);

            // 中文名语言跟随 Steam 客户端（繁中用户取繁中），其余取简中。
            var nameLanguage = this._SteamService.GetCurrentGameLanguage() == "tchinese"
                ? "tchinese"
                : "schinese";
            var localizedNames = new LocalizedNameService(dataDirectory, nameLanguage);

            var library = new GameLibraryViewModel(
                this._SteamService,
                this._GameService,
                familySharing,
                new InstalledGamesService(),
                localizedNames,
                this._MainViewModel,
                this._DialogService);
            this._MainViewModel.Library = library;
            this._MainViewModel.CurrentView = library;
            mainWindow.AttachLibrary(library);

            // --open-game=<appId>：启动后直接进入该游戏的详情页（命令行直达，也便于自动化验证）。
            foreach (var arg in Environment.GetCommandLineArgs())
            {
                if (arg.StartsWith("--open-game=", StringComparison.OrdinalIgnoreCase) == true &&
                    uint.TryParse(arg.Substring(12), System.Globalization.NumberStyles.Integer,
                        System.Globalization.CultureInfo.InvariantCulture, out var openAppId) == true)
                {
                    var gameName = this._SteamService.GetAppData(openAppId, "name");
                    this._MainViewModel.CurrentView = new ViewModels.GameDetailViewModel(
                        openAppId,
                        gameName ?? "App " + openAppId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        this._GameService,
                        this._MainViewModel,
                        this._DialogService);
                    break;
                }
            }
        }

        private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        {
            // WinUI 未处理异常默认静默退出；落盘 + 提示，尽量保住会话。
            try
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sam-winui-crash.txt"),
                    e.Exception.ToString());
            }
            catch
            {
            }

            _ = this._DialogService.ShowErrorAsync("Steam 成就管理器", $"发生未处理的错误：\n{e.Message}");
            e.Handled = true;
        }

        /// <summary>
        /// 启动后台更新检查：延迟启动避让首屏，失败静默（更新检查绝不影响主功能）。
        /// </summary>
        private async Task RunStartupUpdateCheckAsync()
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(3));

                if (this._SettingsService.Settings.AutoCheckUpdate == false)
                {
                    return;
                }

                var result = await this._UpdateService.CheckAsync();
                this._SettingsService.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
                this._SettingsService.Save();

                if (result.Status == UpdateStatus.Available)
                {
                    await UpdateDialog.ShowWithRetryAsync(this._UpdateService, result);
                }
            }
            catch
            {
                // 网络/对话框异常均静默（手动检查入口在设置页）。
            }
        }

        /// <summary>
        /// 更新重启与强制退出的统一清理：进程级退出事件（ProcessExit）在硬退出
        /// 时不保证触发，应用前须先同步回收（统计 Worker 子进程、Steam 互操作）。
        /// </summary>
        internal void PrepareForUpdateExit()
        {
            if (this._CleanedUp == true)
            {
                return;
            }

            this._CleanedUp = true;

            // 兜底清理：详情页统计子进程随 StatsClient.Dispose 终止。
            if (this._MainViewModel?.CurrentView is IDisposable disposable)
            {
                disposable.Dispose();
            }

            this._CallbackTimer?.Stop();
            this._SteamService?.Dispose();
        }

        private void OnProcessExit(object? sender, EventArgs e)
        {
            this.PrepareForUpdateExit();
        }

        /// <summary>初始化失败 → 页面内提示的标题与详情（区分未安装/未运行等场景）。</summary>
        internal static (string Title, string Detail) DescribeInitFailure(SteamInitResult result) => result.Failure switch
        {
            SteamInitFailure.RunningFromSteamDirectory =>
                ("不能从 Steam 目录内运行", "本程序位于 Steam 安装目录内，请移动到其他位置后重试。"),
            SteamInitFailure.GetInstallPath =>
                ("未检测到 Steam", "本机尚未安装 Steam（找不到 Steam 安装信息）。\n请安装 Steam 并登录账号后，点击“重试”。"),
            SteamInitFailure.Load or SteamInitFailure.DllNotFound =>
                ("未检测到 Steam", "Steam 安装不完整，无法加载 steamclient64.dll。\n请重新安装 Steam 后，点击“重试”。"),
            SteamInitFailure.CreateSteamClient =>
                ("初始化 Steam 接口失败", "创建 SteamClient018 失败，请尝试重启 Steam 后点击“重试”。"),
            SteamInitFailure.CreateSteamPipe =>
                ("创建 Steam 管道失败", "请确认 Steam 正在运行，然后点击“重试”。"),
            SteamInitFailure.ConnectToGlobalUser =>
                ("无法连接 Steam 用户", "请确认 Steam 正在运行且已登录。\n（使用家庭共享时也可能出现此错误）\n就绪后点击“重试”。"),
            SteamInitFailure.AppIdMismatch =>
                ("App ID 不匹配", "App ID 与当前 Steam 会话不匹配，请重启 Steam 后重试。"),
            _ => ("初始化 Steam 失败", $"发生未知错误。\n{result.Detail}"),
        };
    }
}
