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
    /// <summary>统计相关纯逻辑（Extra 列、受保护判定、数值解析）。</summary>
    public static class StatisticsService
    {
        [Flags]
        private enum StatExtra
        {
            None = 0,
            IncrementOnly = 1 << 0,
            Protected = 1 << 1,
            UnknownPermission = 1 << 2,
        }

        /// <summary>Extra 列文本（旧版 SAM.Game\Stats\StatInfo.Extra 逐字迁移）。</summary>
        public static string BuildExtra(bool isIncrementOnly, int permission)
        {
            var flags = StatExtra.None;
            flags |= isIncrementOnly == false ? 0 : StatExtra.IncrementOnly;
            flags |= ((permission & 2) != 0) == false ? 0 : StatExtra.Protected;
            flags |= ((permission & ~2) != 0) == false ? 0 : StatExtra.UnknownPermission;
            return flags.ToString();
        }

        /// <summary>受保护统计判定（旧版 Permission &amp; 2 → StatIsProtectedException）。</summary>
        public static bool IsProtected(StatisticData statistic)
        {
            return (statistic.Permission & 2) != 0;
        }

        /// <summary>
        /// 解析用户输入（旧版 int.Parse/float.Parse 为 CurrentCulture；
        /// 新 UI 明确改用 InvariantCulture，避免区域设置导致解析差异）。
        /// </summary>
        public static bool TryParse(string? text, bool isFloat, out long intValue, out double floatValue)
        {
            intValue = 0;
            floatValue = 0;

            if (string.IsNullOrWhiteSpace(text) == true)
            {
                return false;
            }

            text = text.Trim();

            if (isFloat == true)
            {
                if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out floatValue) == false)
                {
                    return false;
                }
            }
            else
            {
                if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) == false ||
                    value < int.MinValue || value > int.MaxValue)
                {
                    return false;
                }
                intValue = value;
            }

            return true;
        }

        /// <summary>当前显示值文本。</summary>
        public static string FormatValue(StatisticData statistic)
        {
            return statistic.IsFloat == true
                ? statistic.FloatValue.ToString("R", CultureInfo.InvariantCulture)
                : statistic.IntValue.ToString(CultureInfo.InvariantCulture);
        }
    }
}
