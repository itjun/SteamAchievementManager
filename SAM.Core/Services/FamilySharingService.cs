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
 *    in a product, an acknowledgment in the original software would be
 *    appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SAM.Core.Services
{
    /// <summary>
    /// 家庭共享探测服务。
    ///
    /// 背景（2026-10-04 实测）：steamclient 的 ISteamApps008 里家庭共享判别只有
    /// "当前进程 SteamAppId 上下文"的无参语义——主进程（appId=0）无法按 appId 查询，
    /// 带参调用形状会静默返回假值；GetEarliestPurchaseUnixTime 对共享游戏返回的是
    /// 借出方的购买时间。唯一可靠的本地途径 = 每个游戏一个独立进程
    /// （`本程序 --probe-appctx=<appId>`，与 stats-worker 同机制），在游戏自身
    /// 上下文里调 IsSubscribedFromFamilySharing()。
    ///
    /// 副作用（因此探测改为仅手动触发）：每个探测子进程会让 Steam 客户端把该游戏
    /// 短暂标记为"运行中"，可能触发已安装游戏的自动更新排队（实测引发过批量下载）。
    /// 启动时只读缓存回填；探测由用户点"检测共享"按钮发起（全量、覆盖缓存）。
    /// 缓存按 SteamID + appId 存 %LOCALAPPDATA%\SAM.Wpf\familysharing.json。
    /// </summary>
    public sealed class FamilySharingService
    {
        /// <summary>缓存有效期。手动模式下仅作自愈上限（数据太久自动作废）。</summary>
        private const int CacheLifetimeDays = 365;

        /// <summary>探测子进程的输出标记行（App.RunAppContextProbe 输出格式）。</summary>
        private const string ProbeMarker = "familySharing(current app) = ";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
        };

        private readonly string _ExePath;
        private readonly string _CachePath;
        private readonly object _CacheLock = new();

        private Dictionary<ulong, Dictionary<uint, CacheEntry>> _Cache = new();

        public FamilySharingService(string exePath, string cacheDirectory)
        {
            this._ExePath = exePath;
            this._CachePath = Path.Combine(cacheDirectory, "familysharing.json");
            this.LoadCache();
        }

        private sealed class CacheEntry
        {
            [JsonPropertyName("s")]
            public bool Shared { get; set; }

            [JsonPropertyName("t")]
            public DateTime AtUtc { get; set; }
        }

        private void LoadCache()
        {
            try
            {
                if (File.Exists(this._CachePath) == false)
                {
                    return;
                }

                var json = File.ReadAllText(this._CachePath);
                var cache = JsonSerializer.Deserialize<Dictionary<ulong, Dictionary<uint, CacheEntry>>>(json, JsonOptions);
                if (cache != null)
                {
                    this._Cache = cache;
                }
            }
            catch
            {
                // 损坏的缓存视为空（下次探测会重建）。
                this._Cache = new();
            }
        }

        private void SaveCache()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(this._CachePath)!);
                File.WriteAllText(this._CachePath, JsonSerializer.Serialize(this._Cache, JsonOptions));
            }
            catch
            {
                // 缓存写失败不影响本次会话结果。
            }
        }

        /// <summary>未过期缓存命中返回 true 并给出 shared；未命中/过期返回 false。</summary>
        public bool TryGetCached(ulong steamId, uint appId, out bool shared)
        {
            lock (this._CacheLock)
            {
                shared = false;
                if (this._Cache.TryGetValue(steamId, out var apps) == false ||
                    apps.TryGetValue(appId, out var entry) == false)
                {
                    return false;
                }

                if (DateTime.UtcNow - entry.AtUtc >= TimeSpan.FromDays(CacheLifetimeDays))
                {
                    return false;
                }

                shared = entry.Shared;
                return true;
            }
        }

        public void Store(ulong steamId, uint appId, bool shared)
        {
            lock (this._CacheLock)
            {
                if (this._Cache.TryGetValue(steamId, out var apps) == false)
                {
                    apps = new();
                    this._Cache[steamId] = apps;
                }

                apps[appId] = new CacheEntry() { Shared = shared, AtUtc = DateTime.UtcNow };
                this.SaveCache();
            }
        }

        /// <summary>
        /// 子进程探测一个游戏：以 --probe-appctx=appId 启动自身副本，解析标记行。
        /// 返回 null 表示失败/超时（不写缓存，下次再试）。
        /// </summary>
        public async Task<bool?> ProbeAsync(uint appId, CancellationToken cancellationToken = default)
        {
            using var process = new Process()
            {
                StartInfo = new ProcessStartInfo()
                {
                    FileName = this._ExePath,
                    Arguments = $"--probe-appctx={appId.ToString(System.Globalization.CultureInfo.InvariantCulture)}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = false,
                    CreateNoWindow = true,
                },
            };

            try
            {
                if (process.Start() == false)
                {
                    return null;
                }
            }
            catch
            {
                return null;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(20));

            try
            {
                var output = await process.StandardOutput.ReadToEndAsync(timeoutCts.Token).ConfigureAwait(false);
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);

                foreach (var line in output.Split('\n'))
                {
                    var index = line.IndexOf(ProbeMarker, StringComparison.Ordinal);
                    if (index < 0)
                    {
                        continue;
                    }

                    return line[(index + ProbeMarker.Length)..].Trim() == "True";
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested == true)
                {
                    throw;
                }

                // 超时：尽力终止子进程，返回未知。
                try { process.Kill(entireProcessTree: true); } catch { }
                return null;
            }
        }
    }
}
