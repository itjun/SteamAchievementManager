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
using System.Net.Http;
using System.Xml.XPath;
using SAM.Core.Models;

namespace SAM.Core.Services
{
    public sealed class GameListResult
    {
        public required List<GameInfo> Games { get; init; }

        /// <summary>true 表示 games.xml 下载失败，已回退到默认列表（Spacewar）。</summary>
        public bool UsedFallback { get; init; }
    }

    /// <summary>
    /// 游戏库服务。迁移自 SAM.Picker\GamePicker.cs：
    /// 列表下载/解析 (99-131)、所有权过滤与命名 (397-429)、过滤谓词 (146-191)、
    /// 图标 URL 三级回退 (338-366)、图标单飞下载与去重 (252-336)。
    /// 线程约定：本服务内部全部 ConfigureAwait(false)；原生调用可来自任意线程
    /// （与旧版 BackgroundWorker 行为一致）。
    /// </summary>
    public sealed class GameService
    {
        private const string GamesListUrl = "https://gib.me/sam/games.xml";

        // WebClient 的默认 User-Agent（保持与旧版一致，避免 CDN 对空 UA 的拦截）。
        private const string UserAgent = "Mozilla/4.0 (compatible; MSIE 8.0; WPF)";

        private static readonly HttpClient Http = CreateHttpClient();

        private readonly SteamService _SteamService;

        private readonly SemaphoreSlim _IconLock = new(1, 1);

        /// <summary>URL → 已下载字节；null 表示该 URL 本会话内已失败（与旧版"不重试"一致）。</summary>
        private readonly Dictionary<string, byte[]?> _IconCache = new();

        public GameService(SteamService steamService)
        {
            this._SteamService = steamService;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            return client;
        }

        public async Task<GameListResult> LoadGamesAsync(
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report("正在下载游戏列表...");

            byte[] bytes;
            try
            {
                bytes = await Http.GetByteArrayAsync(GamesListUrl, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // 旧版行为：下载失败 → AddDefaultGames()（Spacewar），其余静默降级。
                var fallback = new Dictionary<uint, GameInfo>();
                this.AddGame(fallback, 480, "normal"); // Spacewar
                return new GameListResult()
                {
                    Games = fallback.Values.ToList(),
                    UsedFallback = true,
                };
            }

            var pairs = ParseGamesXml(bytes);

            progress?.Report("正在检查游戏所有权...");

            var games = new Dictionary<uint, GameInfo>();
            foreach (var (id, type) in pairs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                this.AddGame(games, id, type);
            }

            return new GameListResult()
            {
                Games = games.Values.ToList(),
                UsedFallback = false,
            };
        }

        /// <summary>手动添加单个游戏（原 OnAddGame：所有权校验后仅显示该游戏）。</summary>
        public GameInfo? AddSingleGame(uint appId)
        {
            if (this._SteamService.IsSubscribedApp(appId) == false)
            {
                return null;
            }

            var info = new GameInfo(appId, "normal");
            info.SetName(this._SteamService.GetAppData(appId, "name"));
            return info;
        }

        public string? GetAppName(uint appId)
        {
            return this._SteamService.GetAppData(appId, "name");
        }

        /// <summary>games.xml 解析（原 DoDownloadList 内联逻辑，逐字迁移）。</summary>
        public static List<(uint Id, string Type)> ParseGamesXml(byte[] bytes)
        {
            var pairs = new List<(uint Id, string Type)>();
            using var stream = new MemoryStream(bytes, false);
            var document = new XPathDocument(stream);
            var nodes = document.CreateNavigator().Select("/games/game");
            while (nodes.MoveNext() == true)
            {
                string type = nodes.Current!.GetAttribute("type", "");
                if (string.IsNullOrEmpty(type) == true)
                {
                    type = "normal";
                }
                pairs.Add(((uint)nodes.Current.ValueAsLong, type));
            }
            return pairs;
        }

        private void AddGame(Dictionary<uint, GameInfo> games, uint id, string type)
        {
            if (games.ContainsKey(id) == true)
            {
                return;
            }

            if (this._SteamService.IsSubscribedApp(id) == false)
            {
                return;
            }

            var info = new GameInfo(id, type);
            info.SetName(this._SteamService.GetAppData(id, "name"));
            games.Add(id, info);
        }

        // 注：家庭共享判别不在加载时做——steamclient 无按 appId 的主进程查询能力
        //（实测带参调用形状恒返回假值），由 FamilySharingService 的子进程探测负责。

        /// <summary>
        /// 搜索 + 类型过滤谓词（原 RefreshGames 146-191 的四复选框改为侧栏分类单选：
        /// categoryKey 为 null 不过滤类型；"all" 全部；已知四类按 type 精确匹配；
        /// "other" 归拢未知类型——与旧版未知类型恒显示的语义一致）。
        /// </summary>
        public static bool MatchesFilter(GameInfo info, string? searchText, string? categoryKey)
        {
            if (categoryKey != null && categoryKey != "all")
            {
                bool wanted = categoryKey == "other"
                    ? info.Type is not ("normal" or "demo" or "mod" or "junk")
                    : info.Type == categoryKey;
                if (wanted == false)
                {
                    return false;
                }
            }

            // 搜索同时匹配原名与本地化名（补中文名后两种输入都要能找到）。
            if (searchText != null)
            {
                bool matched = info.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                if (matched == false && info.LocalizedName != null)
                {
                    matched = info.LocalizedName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                }

                if (matched == false)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 游戏图标 URL 三级回退（原 GetGameImageUrl 338-366）：
        /// small_capsule/{当前语言} → small_capsule/english → logo(.jpg)。
        /// </summary>
        public string? GetImageUrl(uint appId)
        {
            string idText = appId.ToString(CultureInfo.InvariantCulture);

            var language = this._SteamService.GetCurrentGameLanguage() ?? "english";

            var candidate = this._SteamService.GetAppData(appId, $"small_capsule/{language}");
            if (string.IsNullOrEmpty(candidate) == false)
            {
                return $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{idText}/{candidate}";
            }

            if (language != "english")
            {
                candidate = this._SteamService.GetAppData(appId, "small_capsule/english");
                if (string.IsNullOrEmpty(candidate) == false)
                {
                    return $"https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{idText}/{candidate}";
                }
            }

            candidate = this._SteamService.GetAppData(appId, "logo");
            if (string.IsNullOrEmpty(candidate) == false)
            {
                return $"https://cdn.steamstatic.com/steamcommunity/public/images/apps/{idText}/{candidate}.jpg";
            }

            return null;
        }

        /// <summary>
        /// 单飞图标下载：全局串行 + URL 去重 + 会话内失败不重试（原 _LogoWorker 行为）。
        /// </summary>
        public async Task<byte[]?> GetIconAsync(string url, CancellationToken cancellationToken = default)
        {
            await this._IconLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (this._IconCache.TryGetValue(url, out var cached) == true)
                {
                    return cached;
                }

                byte[]? result = null;
                try
                {
                    result = await Http.GetByteArrayAsync(url, cancellationToken).ConfigureAwait(false);
                }
                catch
                {
                    // 失败计入缓存，本会话不重试（与旧版一致）。
                }

                this._IconCache[url] = result;
                return result;
            }
            finally
            {
                this._IconLock.Release();
            }
        }
    }
}
