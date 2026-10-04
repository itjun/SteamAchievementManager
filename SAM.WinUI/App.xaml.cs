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
        private SteamService _SteamService = null!;
        private GameService? _GameService;
        private MainViewModel _MainViewModel = null!;

        private DispatcherQueueTimer? _CallbackTimer;

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

            var mainWindow = new MainWindow(this._MainViewModel, this._SettingsService, this._ThemeService);
            MainHost = mainWindow;
            this._ThemeService.Apply(theme, mainWindow);
            mainWindow.Activate();

            // 初始化 Steam（Picker 模式 appId=0）。
            this._SteamService = new SteamService();
            var initResult = this._SteamService.Initialize(0);
            if (initResult.Success == false)
            {
                _ = this._DialogService.ShowErrorAsync("Steam 成就管理器", DescribeInitFailure(initResult));
                return;
            }

            // Steam 回调泵：等价 WPF 版 100ms DispatcherTimer。
            var timer = mainWindow.DispatcherQueue.CreateTimer();
            timer.Interval = TimeSpan.FromMilliseconds(100);
            timer.Tick += (_, _) => this._SteamService.RunCallbacks();
            timer.Start();
            this._CallbackTimer = timer;

            this._GameService = new GameService(this._SteamService);

            // 游戏库组装：worker/探测子进程 = SAM.Worker.exe；缓存目录 %LOCALAPPDATA%\SAM
            //（SettingsService 已一次性迁移 SAM.Wpf 的 familysharing.json）。
            var workerPath = System.IO.Path.Combine(AppContext.BaseDirectory, "SAM.Worker.exe");
            var dataDirectory = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SAM");
            var familySharing = new FamilySharingService(workerPath, dataDirectory);

            var library = new GameLibraryViewModel(
                this._SteamService,
                this._GameService,
                familySharing,
                new InstalledGamesService(),
                this._MainViewModel,
                this._DialogService);
            this._MainViewModel.Library = library;
            this._MainViewModel.CurrentView = library;
            mainWindow.AttachLibrary(library);
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

        private void OnProcessExit(object? sender, EventArgs e)
        {
            // 兜底清理：详情页统计子进程随 StatsClient.Dispose 终止。
            if (this._MainViewModel?.CurrentView is IDisposable disposable)
            {
                disposable.Dispose();
            }

            this._CallbackTimer?.Stop();
            this._SteamService?.Dispose();
        }

        internal static string DescribeInitFailure(SteamInitResult result) => result.Failure switch
        {
            SteamInitFailure.RunningFromSteamDirectory => "不能从 Steam 安装目录内运行本程序。",
            SteamInitFailure.GetInstallPath => "未能获取 Steam 安装路径，请确认本机已安装 Steam。",
            SteamInitFailure.Load or SteamInitFailure.DllNotFound =>
                "加载 steamclient 失败，请确认 Steam 已正确安装。",
            SteamInitFailure.CreateSteamClient => "初始化 Steam 接口失败（SteamClient018）。",
            SteamInitFailure.CreateSteamPipe => "创建 Steam 管道失败。",
            SteamInitFailure.ConnectToGlobalUser =>
                "无法连接 Steam 用户，请确认 Steam 正在运行且已登录。\n（使用家庭共享时也可能出现此错误）",
            SteamInitFailure.AppIdMismatch => "App ID 与当前 Steam 会话不匹配。",
            _ => $"初始化 Steam 时发生未知错误。\n{result.Detail}",
        };
    }
}
