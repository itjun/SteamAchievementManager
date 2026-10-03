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

using SAM.API;

namespace SAM.Core.Services
{
    /// <summary>Steam 初始化失败原因（不含 UI 文案；由上层映射为用户语言）。</summary>
    public enum SteamInitFailure
    {
        None = 0,

        /// <summary>程序被放在 Steam 安装目录内运行（旧版 Program.cs 的守卫）。</summary>
        RunningFromSteamDirectory,

        /// <summary>未找到 Steam 安装路径（注册表）。</summary>
        GetInstallPath,

        /// <summary>steamclient.dll 加载失败。</summary>
        Load,

        DllNotFound,
        CreateSteamClient,
        CreateSteamPipe,

        /// <summary>Steam 未运行 / 未登录 / 家庭共享占用中。</summary>
        ConnectToGlobalUser,

        AppIdMismatch,
        Unknown,
    }

    public sealed class SteamInitResult
    {
        public SteamInitResult(SteamInitFailure failure, string? detail = null)
        {
            this.Failure = failure;
            this.Detail = detail;
        }

        public bool Success => this.Failure == SteamInitFailure.None;

        public SteamInitFailure Failure { get; }

        /// <summary>底层英文错误信息（来自 SAM.API），仅用于诊断。</summary>
        public string? Detail { get; }
    }

    /// <summary>
    /// Steam 客户端生命周期服务。
    /// 迁移自 SAM.Picker\Program.cs 与 SAM.Game\Program.cs 的初始化与守卫逻辑；
    /// 回调泵（RunCallbacks）必须由 UI 层定时驱动（旧版为 100ms WinForms Timer，
    /// 见 SAM.Picker\GamePicker.cs:431 / SAM.Game\Manager.cs:713）。
    /// steamclient.dll 为 32 位 ThisCall，本进程必须保持 x86。
    /// </summary>
    public sealed class SteamService : IDisposable
    {
        private API.Client? _Client;
        private API.Callbacks.AppDataChanged? _AppDataChangedCallback;
        private API.Callbacks.UserStatsReceived? _UserStatsReceivedCallback;

        public bool IsInitialized => this._Client != null;

        /// <summary>App 元数据变更（回调 id 1001，UI 线程触发——取决于 RunCallbacks 的调用线程）。</summary>
        public event Action<uint>? AppDataChanged;

        /// <summary>用户统计到达（回调 id 1101；触发线程 = 调用 RunCallbacks 的线程）。</summary>
        public event Action<SAM.API.Types.UserStatsReceived>? UserStatsReceived;

        /// <param name="appId">0 = Picker 模式（不设置 SteamAppId 环境变量）；否则按游戏初始化。</param>
        public SteamInitResult Initialize(long appId = 0)
        {
            if (this.IsInitialized == true)
            {
                return new(SteamInitFailure.None);
            }

            string? installPath;
            try
            {
                installPath = Steam.GetInstallPath();
            }
            catch
            {
                installPath = null;
            }

            if (string.IsNullOrEmpty(installPath) == true)
            {
                return new(SteamInitFailure.GetInstallPath);
            }

            // 旧版守卫：拒绝从 Steam 安装目录内运行（Application.StartupPath 比较）。
            if (string.Equals(
                    installPath.TrimEnd('\\', '/'),
                    AppContext.BaseDirectory.TrimEnd('\\', '/'),
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                return new(SteamInitFailure.RunningFromSteamDirectory);
            }

            var client = new API.Client();
            try
            {
                client.Initialize(appId);
            }
            catch (ClientInitializeException e)
            {
                return new(Translate(e.Failure), e.Message);
            }
            catch (DllNotFoundException)
            {
                return new(SteamInitFailure.DllNotFound);
            }

            this._Client = client;
            this._AppDataChangedCallback = client.CreateAndRegisterCallback<API.Callbacks.AppDataChanged>();
            this._AppDataChangedCallback.OnRun += this.OnAppDataChanged;
            this._UserStatsReceivedCallback = client.CreateAndRegisterCallback<API.Callbacks.UserStatsReceived>();
            this._UserStatsReceivedCallback.OnRun += p => this.UserStatsReceived?.Invoke(p);

            return new(SteamInitFailure.None);
        }

        private static SteamInitFailure Translate(ClientInitializeFailure failure) => failure switch
        {
            ClientInitializeFailure.GetInstallPath => SteamInitFailure.GetInstallPath,
            ClientInitializeFailure.Load => SteamInitFailure.Load,
            ClientInitializeFailure.CreateSteamClient => SteamInitFailure.CreateSteamClient,
            ClientInitializeFailure.CreateSteamPipe => SteamInitFailure.CreateSteamPipe,
            ClientInitializeFailure.ConnectToGlobalUser => SteamInitFailure.ConnectToGlobalUser,
            ClientInitializeFailure.AppIdMismatch => SteamInitFailure.AppIdMismatch,
            _ => SteamInitFailure.Unknown,
        };

        private void OnAppDataChanged(SAM.API.Types.AppDataChanged param)
        {
            if (param.Result == false)
            {
                return;
            }

            this.AppDataChanged?.Invoke(param.Id);
        }

        /// <summary>驱动 Steam 回调分发；必须由 UI 层以约 100ms 间隔在 UI 线程调用。</summary>
        public void RunCallbacks()
        {
            this._Client?.RunCallbacks(false);
        }

        public bool IsSubscribedApp(uint appId)
        {
            return this.Client.SteamApps008.IsSubscribedApp(appId);
        }

        // 家庭共享判别：steamclient 的 IsSubscribedFromFamilySharing 仅有"当前进程
        // SteamAppId 上下文"的无参语义，主进程（appId=0）无法按 appId 查询
        //（带参调用形状实测恒返回假值）。按游戏的检测在子进程里做：
        // SteamService.Initialize(appId) 后调 IsFamilySharedCurrentApp()。

        /// <summary>当前进程 SteamAppId 上下文：本游戏是否家庭共享（子进程探测用）。</summary>
        public bool IsFamilySharedCurrentApp()
        {
            return this.Client.SteamApps008.IsFamilySharedCurrentApp();
        }

        /// <summary>购买/获得时间戳（unix 秒）。注意：家庭共享游戏返回的是借出方的时间。</summary>
        public uint GetEarliestPurchaseUnixTime(uint appId)
        {
            return this.Client.SteamApps008.GetEarliestPurchaseUnixTime(appId);
        }

        /// <summary>App 元数据查询（名称、图标路径等）；未命中返回 null。</summary>
        public string? GetAppData(uint appId, string key)
        {
            return this.Client.SteamApps001.GetAppData(appId, key);
        }

        public string? GetCurrentGameLanguage()
        {
            var language = this.Client.SteamApps008.GetCurrentGameLanguage();
            return string.IsNullOrEmpty(language) == true ? null : language;
        }

        // ---- 以下为按游戏上下文使用的 ISteamUserStats / ISteamUser 封装 ----
        // （主进程为 Picker 上下文时不可用；统计工作子进程使用。）

        public ulong GetSteamId()
        {
            return this.Client.SteamUser.GetSteamId();
        }

        public bool RequestUserStats(ulong steamId)
        {
            return this.Client.SteamUserStats.RequestUserStats(steamId) != API.CallHandle.Invalid;
        }

        public bool GetAchievementAndUnlockTime(string name, out bool achieved, out uint unlockTime)
        {
            return this.Client.SteamUserStats.GetAchievementAndUnlockTime(name, out achieved, out unlockTime);
        }

        public bool SetAchievement(string name, bool state)
        {
            return this.Client.SteamUserStats.SetAchievement(name, state);
        }

        public bool GetStatValue(string name, out int value)
        {
            return this.Client.SteamUserStats.GetStatValue(name, out value);
        }

        public bool GetStatValue(string name, out float value)
        {
            return this.Client.SteamUserStats.GetStatValue(name, out value);
        }

        public bool SetStatValue(string name, int value)
        {
            return this.Client.SteamUserStats.SetStatValue(name, value);
        }

        public bool SetStatValue(string name, float value)
        {
            return this.Client.SteamUserStats.SetStatValue(name, value);
        }

        public bool StoreStats()
        {
            return this.Client.SteamUserStats.StoreStats();
        }

        public bool ResetAllStats(bool achievementsToo)
        {
            return this.Client.SteamUserStats.ResetAllStats(achievementsToo);
        }

        private API.Client Client =>
            this._Client ?? throw new InvalidOperationException("Steam 服务尚未初始化。");

        public void Dispose()
        {
            this._AppDataChangedCallback = null;
            this._UserStatsReceivedCallback = null;
            this._Client?.Dispose();
            this._Client = null;
        }
    }
}
