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
 *    claim that you wrote the original software. If you use this product
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
using System.Net;
using System.Text.Json;

namespace SAM.Core.Services
{
    /// <summary>
    /// 游戏名本地化（中文名）。steamclient 的 GetAppData("name") 只返回英文 appinfo 名
    /// （本地化名在嵌套节点，ISteamApps001 不支持路径查询），故走商店 appdetails 接口：
    /// https://store.steampowered.com/api/appdetails?appids={id}&amp;l={language}
    /// 该接口限频（约 200 次/5 分钟，按 IP），纪律：全局串行 + 间隔请求；
    /// 成功名永久缓存，确定性失败（无商店页/已下架）7 天后重试，
    /// 429 触发 5 分钟会话级退避；网络瞬态失败不入缓存。
    /// 缓存：%LOCALAPPDATA%\SAM\localized-names.json（与 familysharing.json 同目录）。
    /// 线程约定：任意线程可调；缓存访问持锁，HTTP 串行于信号量。
    /// </summary>
    public sealed class LocalizedNameService
    {
        private sealed class CacheEntry
        {
            /// <summary>本地化名；null = 确定性失败（无商店页或无名）。</summary>
            public string? Name { get; set; }

            /// <summary>取回时间（unix 秒）——失败项的 7 天重试依据。</summary>
            public long FetchedUnix { get; set; }
        }

        private const string CacheFileName = "localized-names.json";

        private static readonly TimeSpan RequestInterval = TimeSpan.FromMilliseconds(400);

        private static readonly TimeSpan FailureRetryDelay = TimeSpan.FromDays(7);

        private static readonly TimeSpan RateLimitBackoff = TimeSpan.FromMinutes(5);

        private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        // 商店 CDN 拦截空 UA（GameService 同理）；普通浏览器形态即可。
        private const string UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";

        private static readonly HttpClient Http = CreateHttpClient();

        private readonly object _Gate = new();

        private readonly Dictionary<uint, CacheEntry> _Cache = new();

        private readonly string _CachePath;

        private readonly string _Language;

        private readonly SemaphoreSlim _RequestLock = new(1, 1);

        private DateTime _LastRequestUtc = DateTime.MinValue;

        private DateTime _BackoffUntilUtc = DateTime.MinValue;

        public LocalizedNameService(string cacheDirectory, string language)
        {
            this._CachePath = Path.Combine(cacheDirectory, CacheFileName);
            this._Language = language;
            this.Load();
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
            return client;
        }

        /// <summary>该 appId 是否需要发网络请求（未缓存/失败已过期，且不在退避期）。供调用方决定进度展示。</summary>
        public bool NeedsRequest(uint appId)
        {
            lock (this._Gate)
            {
                if (DateTime.UtcNow < this._BackoffUntilUtc)
                {
                    return false;
                }

                return this.TryGetValidCached(appId, out _) == false;
            }
        }

        /// <summary>取本地化名：缓存命中即时返回；否则限频请求商店接口。null = 无中文名或本次失败。</summary>
        public async Task<string?> GetAsync(uint appId, CancellationToken cancellationToken = default)
        {
            lock (this._Gate)
            {
                if (this.TryGetValidCached(appId, out var cached) == true)
                {
                    return cached;
                }

                if (DateTime.UtcNow < this._BackoffUntilUtc)
                {
                    return null;
                }
            }

            await this._RequestLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // 双检：排队期间可能已被并发请求填上。
                lock (this._Gate)
                {
                    if (this.TryGetValidCached(appId, out var cached) == true)
                    {
                        return cached;
                    }

                    if (DateTime.UtcNow < this._BackoffUntilUtc)
                    {
                        return null;
                    }
                }

                var sinceLast = DateTime.UtcNow - this._LastRequestUtc;
                if (sinceLast < RequestInterval)
                {
                    await Task.Delay(RequestInterval - sinceLast, cancellationToken).ConfigureAwait(false);
                }

                this._LastRequestUtc = DateTime.UtcNow;

                string url = "https://store.steampowered.com/api/appdetails?appids=" +
                    appId.ToString(CultureInfo.InvariantCulture) + "&l=" + this._Language;

                byte[] bytes;
                try
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(RequestTimeout);

                    using var response = await Http.GetAsync(url, timeout.Token).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.TooManyRequests)
                    {
                        lock (this._Gate)
                        {
                            this._BackoffUntilUtc = DateTime.UtcNow.Add(RateLimitBackoff);
                        }

                        return null;
                    }

                    if (response.IsSuccessStatusCode == false)
                    {
                        // 5xx 等瞬态失败：不入缓存，下次重试。
                        return null;
                    }

                    bytes = await response.Content.ReadAsByteArrayAsync(timeout.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw; // 调用方取消，向上传播。
                }
                catch (Exception)
                {
                    return null; // 网络/超时等瞬态失败：不入缓存。
                }

                if (TryParseName(bytes, appId, out var name) == true)
                {
                    this.Store(appId, name);
                    return name;
                }

                return null; // 响应非 JSON（CDN 拦截页等）：不入缓存。
            }
            finally
            {
                this._RequestLock.Release();
            }
        }

        /// <summary>appdetails 响应解析：返回 true 表示响应有效（name 可为 null = 无中文版/已下架）。</summary>
        private static bool TryParseName(byte[] bytes, uint appId, out string? name)
        {
            name = null;
            try
            {
                using var document = JsonDocument.Parse(bytes);
                if (document.RootElement.TryGetProperty(
                        appId.ToString(CultureInfo.InvariantCulture), out var app) == false)
                {
                    return false;
                }

                if (app.TryGetProperty("success", out var success) == false ||
                    success.ValueKind != JsonValueKind.True)
                {
                    return true; // success:false = 确定性无商店页 → 负缓存。
                }

                if (app.TryGetProperty("data", out var data) == false ||
                    data.TryGetProperty("name", out var nameNode) == false ||
                    nameNode.ValueKind != JsonValueKind.String)
                {
                    return true;
                }

                var value = nameNode.GetString();
                name = string.IsNullOrWhiteSpace(value) == true ? null : value;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>缓存命中判定：成功项永久有效；失败项 7 天内视为命中（返回 null 且不再请求）。</summary>
        private bool TryGetValidCached(uint appId, out string? name)
        {
            name = null;
            if (this._Cache.TryGetValue(appId, out var entry) == false)
            {
                return false;
            }

            if (entry.Name != null)
            {
                name = entry.Name;
                return true;
            }

            var age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - entry.FetchedUnix;
            return age < FailureRetryDelay.TotalSeconds;
        }

        private void Store(uint appId, string? name)
        {
            lock (this._Gate)
            {
                this._Cache[appId] = new CacheEntry()
                {
                    Name = name,
                    FetchedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                };

                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(this._CachePath)!);
                    File.WriteAllText(
                        this._CachePath, JsonSerializer.Serialize(this._Cache, JsonOptions));
                }
                catch
                {
                    // 缓存写失败不影响本次结果（下次重取）。
                }
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(this._CachePath) == false)
                {
                    return;
                }

                var cache = JsonSerializer.Deserialize<Dictionary<uint, CacheEntry>>(
                    File.ReadAllText(this._CachePath), JsonOptions);
                if (cache == null)
                {
                    return;
                }

                lock (this._Gate)
                {
                    foreach (var pair in cache)
                    {
                        this._Cache[pair.Key] = pair.Value;
                    }
                }
            }
            catch
            {
                // 缓存损坏按空缓存处理。
            }
        }
    }
}
