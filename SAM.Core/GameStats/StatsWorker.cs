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

using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using SAM.Core.Schema;
using SAM.Core.Services;
using APITypes = SAM.API.Types;

namespace SAM.Core.GameStats
{
    /// <summary>
    /// 统计工作子进程：以目标游戏的 appId 初始化 Steam（这正是旧版按游戏
    /// 启动 SAM.Game.exe 的原因），通过 stdin/stdout 的 JSON-lines 处理请求。
    /// 业务迁移自 SAM.Game\Manager.cs（LoadUserGameStatsSchema 206-365、
    /// GetAchievements 441-538、GetStatistics 540-583、Store 流程 605-794、
    /// OnResetAllStats 829-862）。
    /// </summary>
    public sealed class StatsWorker
    {
        private readonly long _AppId;
        private readonly SteamService _Steam;
        private readonly ManualResetEventSlim _StatsReceived = new();
        // 仅由请求处理线程读写（RunCallbacks 也在该线程），无需 volatile。
        private APITypes.UserStatsReceived _LastStatsReceived;

        private StatsWorker(long appId, SteamService steam)
        {
            this._AppId = appId;
            this._Steam = steam;
        }

        /// <summary>子进程入口：阻塞处理 stdin 请求直到流关闭。返回进程退出码。</summary>
        public static int Run(long appId)
        {
            try
            {
                Console.OutputEncoding = System.Text.Encoding.UTF8;
            }
            catch
            {
                // 输出编码设置失败不影响 JSON（ASCII 安全子集）。
            }

            using var steam = new SteamService();
            var init = steam.Initialize(appId);
            if (init.Success == false)
            {
                WriteResponse(new WorkerResponse()
                {
                    Id = 0,
                    Ok = false,
                    Error = "steam init failed: " + init.Failure,
                });
                return 1;
            }

            var worker = new StatsWorker(appId, steam);
            steam.UserStatsReceived += p =>
            {
                worker._LastStatsReceived = p;
                worker._StatsReceived.Set();
            };

            string? line;
            while ((line = Console.In.ReadLine()) != null)
            {
                if (string.IsNullOrWhiteSpace(line) == true)
                {
                    continue;
                }

                WorkerResponse response;
                try
                {
                    var request = JsonSerializer.Deserialize<WorkerRequest>(line, ProtocolJson.Options);
                    response = request == null
                        ? new WorkerResponse() { Id = -1, Ok = false, Error = "bad request" }
                        : worker.Handle(request);
                }
                catch (Exception exception)
                {
                    response = new WorkerResponse()
                    {
                        Id = -1,
                        Ok = false,
                        Error = exception.Message,
                    };
                }

                WriteResponse(response);
            }

            return 0;
        }

        private static void WriteResponse(WorkerResponse response)
        {
            Console.Out.WriteLine(JsonSerializer.Serialize(response, ProtocolJson.Options));
            Console.Out.Flush();
        }

        private WorkerResponse Handle(WorkerRequest request) => request.Cmd switch
        {
            "get" => this.HandleGet(request),
            "store" => this.HandleStore(request),
            "resetAll" => this.HandleResetAll(request),
            _ => new WorkerResponse() { Id = request.Id, Ok = false, Error = "unknown cmd: " + request.Cmd },
        };

        private WorkerResponse HandleGet(WorkerRequest request)
        {
            var error = this.EnsureStatsReceived();
            if (error != null)
            {
                return new WorkerResponse() { Id = request.Id, Ok = false, Error = error };
            }

            var (payload, loadError) = this.BuildPayload();
            return new WorkerResponse()
            {
                Id = request.Id,
                Ok = loadError == null,
                Error = loadError,
                Payload = payload,
            };
        }

        /// <summary>保存流程（逐字保留旧版 OnStore 语义：逐项设置，任一失败立即中止；无论成败最后刷新）。</summary>
        private WorkerResponse HandleStore(WorkerRequest request)
        {
            string? error = null;

            if (request.Achievements != null)
            {
                foreach (var change in request.Achievements)
                {
                    if (this._Steam.SetAchievement(change.Name, change.Achieved) == false)
                    {
                        error = "set achievement failed: " + change.Name;
                        break;
                    }
                }
            }

            if (error == null && request.Stats != null)
            {
                foreach (var change in request.Stats)
                {
                    bool ok = change.IsFloat
                        ? this._Steam.SetStatValue(change.Name, (float)change.FloatValue)
                        : this._Steam.SetStatValue(change.Name, (int)change.IntValue);
                    if (ok == false)
                    {
                        error = "set stat failed: " + change.Name;
                        break;
                    }
                }
            }

            if (error == null && this._Steam.StoreStats() == false)
            {
                error = "store failed";
            }

            // 旧版行为：无论成败都 RefreshStats 重新拉取。
            var refreshError = this.EnsureStatsReceived();
            var (payload, loadError) = refreshError != null ? (null, refreshError) : this.BuildPayload();

            return new WorkerResponse()
            {
                Id = request.Id,
                Ok = error == null,
                Error = error ?? loadError,
                Payload = payload,
            };
        }

        private WorkerResponse HandleResetAll(WorkerRequest request)
        {
            string? error = this._Steam.ResetAllStats(request.AchievementsToo) == false
                ? "reset failed"
                : null;

            var refreshError = this.EnsureStatsReceived();
            var (payload, loadError) = refreshError != null ? (null, refreshError) : this.BuildPayload();

            return new WorkerResponse()
            {
                Id = request.Id,
                Ok = error == null,
                Error = error ?? loadError,
                Payload = payload,
            };
        }

        /// <summary>RequestUserStats + 泵回调等待 UserStatsReceived（旧版 RefreshStats 419-437 + Timer 泵）。</summary>
        private string? EnsureStatsReceived()
        {
            this._StatsReceived.Reset();
            this._LastStatsReceived = default;

            var steamId = this._Steam.GetSteamId();
            if (this._Steam.RequestUserStats(steamId) == false)
            {
                return "failed to request user stats";
            }

            var watch = Stopwatch.StartNew();
            while (this._StatsReceived.IsSet == false)
            {
                this._Steam.RunCallbacks();
                if (watch.Elapsed > TimeSpan.FromSeconds(15))
                {
                    return "timeout waiting for user stats";
                }
                Thread.Sleep(50);
            }

            var result = this._LastStatsReceived.Result;
            return result == 1 ? null : "stats retrieve failed: result=" + result.ToString(CultureInfo.InvariantCulture);
        }

        private sealed record AchievementDef(
            string Id,
            string Name,
            string Description,
            string IconNormal,
            string IconLocked,
            bool IsHidden,
            int Permission);

        private sealed record StatDef(string Id, string Name, bool IsFloat, bool IncrementOnly, int Permission);

        private (GameStatsPayload?, string?) BuildPayload()
        {
            var language = this._Steam.GetCurrentGameLanguage() ?? "english";

            var (achievementDefs, statDefs, schemaError) = this.LoadSchema(language);
            if (schemaError != null)
            {
                return (null, schemaError);
            }

            var payload = new GameStatsPayload()
            {
                Language = language,
            };

            foreach (var def in achievementDefs)
            {
                if (this._Steam.GetAchievementAndUnlockTime(def.Id, out var achieved, out var unlockTime) == false)
                {
                    continue;
                }

                payload.Achievements.Add(new AchievementData()
                {
                    Id = def.Id,
                    Name = def.Name,
                    Description = def.Description,
                    IconNormal = string.IsNullOrEmpty(def.IconNormal) == true ? null : def.IconNormal,
                    IconLocked = string.IsNullOrEmpty(def.IconLocked) == true ? def.IconNormal : def.IconLocked,
                    IsHidden = def.IsHidden,
                    Permission = def.Permission,
                    IsAchieved = achieved,
                    UnlockTime = achieved && unlockTime > 0 ? unlockTime : 0,
                });
            }

            foreach (var def in statDefs)
            {
                if (def.IsFloat == true)
                {
                    if (this._Steam.GetStatValue(def.Id, out float value) == false)
                    {
                        continue;
                    }
                    payload.Stats.Add(new StatisticData()
                    {
                        Id = def.Id,
                        Name = def.Name,
                        IsFloat = true,
                        FloatValue = value,
                        IsIncrementOnly = def.IncrementOnly,
                        Permission = def.Permission,
                    });
                }
                else
                {
                    if (this._Steam.GetStatValue(def.Id, out int value) == false)
                    {
                        continue;
                    }
                    payload.Stats.Add(new StatisticData()
                    {
                        Id = def.Id,
                        Name = def.Name,
                        IsFloat = false,
                        IntValue = value,
                        IsIncrementOnly = def.IncrementOnly,
                        Permission = def.Permission,
                    });
                }
            }

            return (payload, null);
        }

        /// <summary>读取本地 UserGameStatsSchema_{appId}.bin（旧版 LoadUserGameStatsSchema 逐字迁移）。</summary>
        private (List<AchievementDef>, List<StatDef>, string?) LoadSchema(string language)
        {
            string? path;
            try
            {
                var installPath = SAM.API.Steam.GetInstallPath();
                if (string.IsNullOrEmpty(installPath) == true)
                {
                    return (new(), new(), "steam install path not found");
                }
                path = Path.Combine(
                    installPath,
                    "appcache",
                    "stats",
                    $"UserGameStatsSchema_{this._AppId.ToString(CultureInfo.InvariantCulture)}.bin");
                if (File.Exists(path) == false)
                {
                    return (new(), new(), "schema file not found (需要先运行过该游戏)");
                }
            }
            catch (Exception exception)
            {
                return (new(), new(), "schema path error: " + exception.Message);
            }

            var kv = KeyValue.LoadAsBinary(path);
            if (kv == null)
            {
                return (new(), new(), "failed to parse schema");
            }

            var achievements = new List<AchievementDef>();
            var stats = new List<StatDef>();

            var root = kv[this._AppId.ToString(CultureInfo.InvariantCulture)]["stats"];
            if (root.Valid == false || root.Children == null)
            {
                return (new(), new(), "invalid schema root");
            }

            foreach (var stat in root.Children)
            {
                if (stat.Valid == false)
                {
                    continue;
                }

                APITypes.UserStatType type;

                // 新格式：type 为字符串
                var typeNode = stat["type"];
                if (typeNode.Valid == true && typeNode.Type == KeyValueType.String)
                {
                    if (Enum.TryParse(typeNode.Value as string, true, out type) == false)
                    {
                        type = APITypes.UserStatType.Invalid;
                    }
                }
                else
                {
                    type = APITypes.UserStatType.Invalid;
                }

                // 旧格式：type_int
                if (type == APITypes.UserStatType.Invalid)
                {
                    var typeIntNode = stat["type_int"];
                    var rawType = typeIntNode.Valid == true
                        ? typeIntNode.AsInteger(0)
                        : typeNode.AsInteger(0);
                    type = (APITypes.UserStatType)rawType;
                }

                switch (type)
                {
                    case APITypes.UserStatType.Invalid:
                    {
                        break;
                    }

                    case APITypes.UserStatType.Integer:
                    {
                        var id = stat["name"].AsString("");
                        stats.Add(new StatDef(
                            id,
                            GetLocalizedString(stat["display"]["name"], language, id),
                            IsFloat: false,
                            IncrementOnly: stat["incrementonly"].AsBoolean(false),
                            Permission: stat["permission"].AsInteger(0)));
                        break;
                    }

                    case APITypes.UserStatType.Float:
                    case APITypes.UserStatType.AverageRate:
                    {
                        var id = stat["name"].AsString("");
                        stats.Add(new StatDef(
                            id,
                            GetLocalizedString(stat["display"]["name"], language, id),
                            IsFloat: true,
                            IncrementOnly: stat["incrementonly"].AsBoolean(false),
                            Permission: stat["permission"].AsInteger(0)));
                        break;
                    }

                    case APITypes.UserStatType.Achievements:
                    case APITypes.UserStatType.GroupAchievements:
                    {
                        if (stat.Children != null)
                        {
                            foreach (var bits in stat.Children.Where(
                                b => string.Compare(b.Name, "bits", StringComparison.InvariantCultureIgnoreCase) == 0))
                            {
                                if (bits.Valid == false || bits.Children == null)
                                {
                                    continue;
                                }

                                foreach (var bit in bits.Children)
                                {
                                    var id = bit["name"].AsString("");
                                    achievements.Add(new AchievementDef(
                                        id,
                                        GetLocalizedString(bit["display"]["name"], language, id),
                                        GetLocalizedString(bit["display"]["desc"], language, ""),
                                        bit["display"]["icon"].AsString(""),
                                        bit["display"]["icon_gray"].AsString(""),
                                        bit["display"]["hidden"].AsBoolean(false),
                                        bit["permission"].AsInteger(0)));
                                }
                            }
                        }

                        break;
                    }

                    default:
                    {
                        throw new InvalidOperationException("invalid stat type");
                    }
                }
            }

            return (achievements, stats, null);
        }

        /// <summary>旧版 GetLocalizedString（Manager.cs:180-204）逐字迁移。</summary>
        private static string GetLocalizedString(KeyValue kv, string language, string defaultValue)
        {
            var name = kv[language].AsString("");
            if (string.IsNullOrEmpty(name) == false)
            {
                return name;
            }

            if (language != "english")
            {
                name = kv["english"].AsString("");
                if (string.IsNullOrEmpty(name) == false)
                {
                    return name;
                }
            }

            name = kv.AsString("");
            if (string.IsNullOrEmpty(name) == false)
            {
                return name;
            }

            return defaultValue;
        }
    }
}
