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
using System.Windows;
using System.Windows.Threading;
using SAM.Core.GameStats;
using SAM.Core.Services;
using SAM.WpfApp.Models;
using SAM.WpfApp.Services;
using SAM.WpfApp.ViewModels;
using SAM.WpfApp.Views.Pages;

namespace SAM.WpfApp
{
    public partial class App : Application
    {
        private SettingsService _SettingsService = null!;
        private ThemeService _ThemeService = null!;
        private SteamService _SteamService = null!;
        private GameService _GameService = null!;
        private MainViewModel _MainViewModel = null!;

        private DispatcherTimer? _CallbackTimer;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 统计工作子进程分支：Steam 的 ISteamUserStats 绑定进程 SteamAppId 上下文，
            // 查看某游戏的成就/统计时主进程以本模式启动隐藏副本（同旧版按游戏开 SAM.Game.exe）。
            foreach (var arg in e.Args)
            {
                if (arg.StartsWith("--stats-worker=", StringComparison.OrdinalIgnoreCase) == true &&
                    long.TryParse(arg.Substring(15), NumberStyles.Integer, CultureInfo.InvariantCulture, out var workerAppId) == true)
                {
                    this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    this.Shutdown(StatsWorker.Run(workerAppId));
                    return;
                }
            }

            // 家庭共享判别原生调用探测：
            // --probe-familysharing=id1,id2 → 主进程上下文诊断（自购/购买时间戳）
            // --probe-appctx=<appId> → 按游戏上下文探测（FamilySharingService 解析其输出）
            foreach (var arg in e.Args)
            {
                if (arg.StartsWith("--probe-familysharing=", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var ids = arg.Substring("--probe-familysharing=".Length)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(text => uint.Parse(text, CultureInfo.InvariantCulture))
                        .ToArray();
                    this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    this.Shutdown(this.RunFamilySharingProbe(ids));
                    return;
                }

                // 按游戏上下文探测（每游戏一个独立进程，SteamAppId=该游戏）。
                if (arg.StartsWith("--probe-appctx=", StringComparison.OrdinalIgnoreCase) == true &&
                    uint.TryParse(arg.Substring("--probe-appctx=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ctxAppId) == true)
                {
                    this.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                    this.Shutdown(this.RunAppContextProbe(ctxAppId));
                    return;
                }
            }

            // 开发期兜底：未处理异常给出提示而不是静默退出。
            this.DispatcherUnhandledException += this.OnDispatcherUnhandledException;

            this._SettingsService = new SettingsService();
            this._ThemeService = new ThemeService();

            var theme = this._SettingsService.Settings.Theme;

            // 调试辅助：--theme=dark|light|system 覆盖已保存的主题（不写回设置文件）。
            foreach (var arg in e.Args)
            {
                if (arg.StartsWith("--theme=", StringComparison.OrdinalIgnoreCase) == true &&
                    System.Enum.TryParse(arg.Substring(8), true, out ThemeMode parsed) == true)
                {
                    theme = parsed;
                }
            }

            // 初始化 Steam（Picker 模式 appId=0），等价旧版 Program.cs 守卫与初始化。
            this._SteamService = new SteamService();
            var initResult = this._SteamService.Initialize(0);
            if (initResult.Success == false)
            {
                MessageBox.Show(
                    DescribeInitFailure(initResult),
                    "Steam 成就管理器",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                this.Shutdown(-1);
                return;
            }

            // Steam 回调泵：等价旧版 100ms WinForms Timer（GamePicker.cs:431-436 / Manager.cs:713-718）。
            this._CallbackTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(100),
            };
            this._CallbackTimer.Tick += (_, _) => this._SteamService.RunCallbacks();
            this._CallbackTimer.Start();

            this._GameService = new GameService(this._SteamService);
            this._MainViewModel = new MainViewModel();

            // 家庭共享探测：以自身副本按游戏上下文检测（见 FamilySharingService 类注释）。
            var familySharing = new FamilySharingService(
                Environment.ProcessPath ?? throw new InvalidOperationException("无法定位当前程序路径。"),
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SAM.Wpf"));

            var libraryViewModel = new GameLibraryViewModel(
                this._SteamService,
                this._GameService,
                familySharing,
                new InstalledGamesService(),
                this._MainViewModel);
            this._MainViewModel.Library = libraryViewModel;
            this._MainViewModel.CurrentView = libraryViewModel;

            // --open-game=<appId>：启动后直接进入该游戏的详情页（命令行直达，也便于自动化验证）。
            foreach (var arg in e.Args)
            {
                if (arg.StartsWith("--open-game=", StringComparison.OrdinalIgnoreCase) == true &&
                    uint.TryParse(arg.Substring(12), NumberStyles.Integer, CultureInfo.InvariantCulture, out var openAppId) == true)
                {
                    var gameName = this._SteamService.GetAppData(openAppId, "name");
                    this._MainViewModel.CurrentView = new GameDetailViewModel(
                        openAppId,
                        gameName ?? "App " + openAppId.ToString(CultureInfo.InvariantCulture),
                        this._GameService,
                        this._MainViewModel);
                    break;
                }
            }

            var pageService = new PageService()
                .Register(typeof(GameLibraryPage), () => new GameLibraryPage(this._MainViewModel))
                .Register(typeof(SettingsPage), () => new SettingsPage(
                    new SettingsViewModel(this._SettingsService, this._ThemeService)));

            var mainWindow = new MainWindow(pageService, this._ThemeService, theme, this._MainViewModel);
            this.MainWindow = mainWindow;
            mainWindow.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // 详情页的统计子进程随 StatsClient.Dispose 终止；
            // 主进程退出也会关闭子进程的 stdin 使其自然退出，这里做兜底清理。
            if (this._MainViewModel?.CurrentView is GameDetailViewModel detail)
            {
                detail.Dispose();
            }

            this._CallbackTimer?.Stop();
            this._SteamService?.Dispose();
            base.OnExit(e);
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint SetErrorMode(uint mode);

        /// <summary>探测进程禁用硬错误弹窗（0xC0000005 崩溃时静默退出，不再阻塞等点击）。</summary>
        private static void SuppressCrashDialogs()
        {
            const uint SEM_FAILCRITICALERRORS = 0x0001;
            const uint SEM_NOGPFAULTERRORBOX = 0x0002;
            _ = SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
        }

        /// <summary>逐行输出探测结果（stdout 重定向时可见）；每行即刷，崩溃时可定位到最后一次调用。</summary>
        private int RunFamilySharingProbe(uint[] ids)
        {
            SuppressCrashDialogs();
            var probe = new SteamService();
            var init = probe.Initialize(0);
            if (init.Success == false)
            {
                Console.WriteLine($"init failed: {init.Failure} {init.Detail}");
                return 2;
            }

            var me = probe.GetSteamId();
            Console.WriteLine($"me = {me}");
            foreach (var id in ids)
            {
                Console.WriteLine($"[{id}] subscribed = {probe.IsSubscribedApp(id)}");
                Console.Out.Flush();
                Console.WriteLine($"[{id}] purchaseTime = {probe.GetEarliestPurchaseUnixTime(id)}");
                Console.Out.Flush();
            }

            probe.Dispose();
            return 0;
        }

        /// <summary>
        /// 按游戏上下文探测（SteamAppId=appId 的独立进程）。
        /// 输出格式被 FamilySharingService 解析（ProbeMarker），改动需两边同步。
        /// </summary>
        private int RunAppContextProbe(uint appId)
        {
            SuppressCrashDialogs();
            Console.WriteLine($"probe starting, appId={appId}");
            Console.Out.Flush();
            var probe = new SteamService();
            var init = probe.Initialize(appId);
            if (init.Success == false)
            {
                Console.WriteLine($"init(app={appId}) failed: {init.Failure} {init.Detail}");
                Console.Out.Flush();
                return 2;
            }

            Console.WriteLine("init ok");
            Console.Out.Flush();
            Console.WriteLine($"me = {probe.GetSteamId()}");
            Console.Out.Flush();
            Console.WriteLine($"[{appId}] subscribed = {probe.IsSubscribedApp(appId)}");
            Console.Out.Flush();
            Console.WriteLine($"[{appId}] familySharing(current app) = {probe.IsFamilySharedCurrentApp()}");
            Console.Out.Flush();
            // GetAppOwner 槽位带参/无参两种调用形状均实测 0xC0000005，已弃用。
            Console.WriteLine($"[{appId}] purchaseTime = {probe.GetEarliestPurchaseUnixTime(appId)}");
            Console.Out.Flush();

            probe.Dispose();
            return 0;
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"发生未处理的错误：\n{e.Exception.Message}",
                "Steam 成就管理器",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            e.Handled = true;
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
