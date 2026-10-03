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

using System.Text.Json;
using System.Text.Json.Serialization;

namespace SAM.Core.GameStats
{
    /// <summary>
    /// 主进程 ↔ 统计工作子进程 的 JSON-lines 协议（stdio，一行一个 JSON 对象）。
    /// 背景约束：ISteamUserStats 绑定进程的 SteamAppId 上下文（实验证实同进程
    /// 换 appId 重建 Client 会得到 appID mismatch），因此按游戏启动隐藏子进程。
    /// </summary>
    public static class ProtocolJson
    {
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
    }

    public sealed class WorkerRequest
    {
        /// <summary>"get" | "store" | "resetAll"</summary>
        public string Cmd { get; set; } = "";

        public int Id { get; set; }

        /// <summary>store：要提交的成就变更（仅差异项，语义同旧版 StoreAchievements）。</summary>
        public List<AchievementChange>? Achievements { get; set; }

        /// <summary>store：要提交的统计变更（仅修改项）。</summary>
        public List<StatChange>? Stats { get; set; }

        /// <summary>resetAll：是否连同成就一起重置。</summary>
        public bool AchievementsToo { get; set; }
    }

    public sealed class WorkerResponse
    {
        public int Id { get; set; }

        public bool Ok { get; set; }

        public string? Error { get; set; }

        /// <summary>get/store/resetAll 成功或失败后的最新数据（失败但能重读时也会带上，等价旧版 RefreshStats）。</summary>
        public GameStatsPayload? Payload { get; set; }
    }

    public sealed class AchievementChange
    {
        public string Name { get; set; } = "";
        public bool Achieved { get; set; }
    }

    public sealed class StatChange
    {
        public string Name { get; set; } = "";
        public bool IsFloat { get; set; }
        public long IntValue { get; set; }
        public double FloatValue { get; set; }
    }

    public sealed class GameStatsPayload
    {
        public string Language { get; set; } = "english";
        public List<AchievementData> Achievements { get; set; } = new();
        public List<StatisticData> Stats { get; set; } = new();
    }

    public sealed class AchievementData
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public string? IconNormal { get; set; }
        public string? IconLocked { get; set; }
        public bool IsHidden { get; set; }
        public int Permission { get; set; }

        /// <summary>Steam 侧当前状态。</summary>
        public bool IsAchieved { get; set; }

        /// <summary>Unix 秒；0 = 无。</summary>
        public long UnlockTime { get; set; }
    }

    public sealed class StatisticData
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public bool IsFloat { get; set; }
        public long IntValue { get; set; }
        public double FloatValue { get; set; }
        public bool IsIncrementOnly { get; set; }
        public int Permission { get; set; }
    }
}
