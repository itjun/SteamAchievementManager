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
using SAM.Core.GameStats;

namespace SAM.Core.Services
{
    /// <summary>成就相关纯逻辑（图标 URL、过滤谓词、受保护判定）。</summary>
    public static class AchievementService
    {
        /// <summary>成就图标 URL（旧版 Manager.cs:170 语义，图标文件名来自 schema）。</summary>
        public static string? GetIconUrl(uint appId, string? filename)
        {
            if (string.IsNullOrEmpty(filename) == true)
            {
                return null;
            }

            return string.Format(
                CultureInfo.InvariantCulture,
                "https://cdn.steamstatic.com/steamcommunity/public/images/apps/{0}/{1}",
                appId,
                filename);
        }

        /// <summary>受保护成就判定（旧版 Permission &amp; 3，Manager.cs:881）。</summary>
        public static bool IsProtected(AchievementData achievement)
        {
            return (achievement.Permission & 3) != 0;
        }

        /// <summary>成就过滤谓词（旧版 GetAchievements 443-488：名称或描述子串 + 状态筛选互斥）。</summary>
        public static bool MatchesFilter(AchievementData achievement, bool displayAchieved, string? searchText, AchievementFilter filter)
        {
            if (filter == AchievementFilter.Unlocked && displayAchieved == false)
            {
                return false;
            }

            if (filter == AchievementFilter.Locked && displayAchieved == true)
            {
                return false;
            }

            if (searchText != null &&
                achievement.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0 &&
                achievement.Description.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            return true;
        }
    }

    public enum AchievementFilter
    {
        All = 0,
        Unlocked = 1,
        Locked = 2,
    }
}
