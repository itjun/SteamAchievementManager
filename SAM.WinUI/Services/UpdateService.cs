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

using System.IO;
using System.Net.Http;
using System.Text.Json;
using Velopack;
using Velopack.Sources;

namespace SAM.WinUIApp.Services
{
    public enum UpdateStatus
    {
        UpToDate = 0,
        Available = 1,
        /// <summary>未以 Velopack 发布包方式运行（VS 直跑 / 裸跑 publish 输出）。</summary>
        NotInstalled = 2,
        Failed = 3,
    }

    public sealed class UpdateCheckResult
    {
        public UpdateStatus Status { get; init; }

        /// <summary>Available 时有效：当前版本低于 Release 声明的最低可用版本。</summary>
        public bool IsRequired { get; init; }

        public string? NewVersion { get; init; }
        public string? Notes { get; init; }
        public string? ErrorMessage { get; init; }

        /// <summary>Velopack 句柄，下载/应用阶段使用。</summary>
        public UpdateInfo? Update { get; init; }
    }

    /// <summary>Release 资产 update-manifest.json（CI 由仓库根 update-policy.json 生成）。</summary>
    public sealed class UpdateManifest
    {
        public string? Version { get; set; }
        public string? MinimumRequired { get; set; }
        public string? Notes { get; set; }
        public string? ReleaseUrl { get; set; }
    }

    /// <summary>
    /// 自动更新（Velopack + GitHub Releases）：检查 → 弹窗询问（强制/可选）→
    /// 带进度下载（增量优先）→ 应用并重启。强制与否由 Release 里
    /// update-manifest.json 的 minimumRequired 决定，与 Velopack 版本比较解耦。
    /// </summary>
    public sealed class UpdateService
    {
        private const string RepoUrl = "https://github.com/itjun/SteamAchievementManager";
        private const string ManifestUrl = RepoUrl + "/releases/latest/download/update-manifest.json";

        /// <summary>
        /// 覆盖更新源为本地 vpk pack 产物目录（含 releases.*.json 与 update-manifest.json），
        /// 供不发 GitHub 的全链路更新测试；未设置时走 GitHub。
        /// </summary>
        private static readonly string? SourceOverride = Environment.GetEnvironmentVariable("SAM_UPDATE_SOURCE");

        private static readonly JsonSerializerOptions ManifestJsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
        };

        private static readonly HttpClient Http = CreateHttpClient();

        private readonly Lazy<UpdateManager?> _Manager = new(CreateManager);

        public static string CurrentVersion
        {
            get
            {
                var version = typeof(App).Assembly.GetName().Version;
                return version == null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(15);
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent", $"SAM/{CurrentVersion} ({RepoUrl})");
            return client;
        }

        private static UpdateManager? CreateManager()
        {
            try
            {
                // 本地目录走文件源；GitHub 走 GithubSource（匿名 REST API 取最新
                // 非 prerelease Release，BrowserDownloadUrl 下载不受 API 限额影响）。
                return SourceOverride != null
                    ? new UpdateManager(SourceOverride)
                    : new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
            }
            catch
            {
                // 无 Velopack 定位信息（未安装运行）等：视为不可更新。
                return null;
            }
        }

        public bool IsInstalled => this._Manager.Value?.IsInstalled == true;

        public async Task<UpdateCheckResult> CheckAsync()
        {
            var manager = this._Manager.Value;
            if (manager == null || manager.IsInstalled == false)
            {
                return new UpdateCheckResult { Status = UpdateStatus.NotInstalled };
            }

            try
            {
                var update = await manager.CheckForUpdatesAsync();
                if (update == null)
                {
                    return new UpdateCheckResult { Status = UpdateStatus.UpToDate };
                }

                // 清单拉不到只降级为可选更新，不阻断检查结果。
                var manifest = await this.FetchManifestAsync();
                var isRequired = manifest?.MinimumRequired != null
                    && TryParseVersion(manifest.MinimumRequired, out var minimum)
                    && TryParseVersion(CurrentVersion, out var current)
                    && minimum > current;

                return new UpdateCheckResult
                {
                    Status = UpdateStatus.Available,
                    IsRequired = isRequired,
                    NewVersion = update.TargetFullRelease.Version.ToString(),
                    Notes = manifest?.Notes,
                    Update = update,
                };
            }
            catch (Exception e)
            {
                return new UpdateCheckResult { Status = UpdateStatus.Failed, ErrorMessage = e.Message };
            }
        }

        /// <summary>下载更新（增量优先，失败自动回退全量）。进度 0-100。</summary>
        public async Task DownloadAsync(UpdateInfo update, IProgress<int> progress)
        {
            var manager = this._Manager.Value
                ?? throw new InvalidOperationException("应用未以安装方式运行。");
            await manager.DownloadUpdatesAsync(update, progress.Report);
        }

        /// <summary>应用更新并重启（透传原启动参数，剔除 Velopack 自身参数）。正常不返回。</summary>
        public void ApplyAndRestart(UpdateInfo update)
        {
            var manager = this._Manager.Value
                ?? throw new InvalidOperationException("应用未以安装方式运行。");

            var restartArgs = Environment.GetCommandLineArgs()
                .Skip(1)
                .Where(argument =>
                    argument.StartsWith("--veloapp", StringComparison.OrdinalIgnoreCase) == false &&
                    argument.StartsWith("--squirrel", StringComparison.OrdinalIgnoreCase) == false)
                .ToArray();
            manager.ApplyUpdatesAndRestart(update, restartArgs);
        }

        private async Task<UpdateManifest?> FetchManifestAsync()
        {
            try
            {
                if (SourceOverride != null)
                {
                    var path = Path.Combine(SourceOverride, "update-manifest.json");
                    return File.Exists(path) == true
                        ? JsonSerializer.Deserialize<UpdateManifest>(File.ReadAllText(path), ManifestJsonOptions)
                        : null;
                }

                using var response = await Http.GetAsync(ManifestUrl);
                response.EnsureSuccessStatusCode();
                var json = await response.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<UpdateManifest>(json, ManifestJsonOptions);
            }
            catch
            {
                return null;
            }
        }

        private static bool TryParseVersion(string text, out Version version) =>
            Version.TryParse(text.TrimStart('v', 'V'), out version!);
    }
}
