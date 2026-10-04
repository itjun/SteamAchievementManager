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

using System.Globalization;
using SAM.Core.GameStats;
using SAM.Core.Services;

namespace SAM.Worker
{
    /// <summary>
    /// Steam 子进程宿主：统计 worker（--stats-worker）与家庭共享探测（--probe-*）。
    /// 从 UI 进程拆出为独立 exe——WinUI 3 入口经 Windows App SDK 引导后才有控制权，
    /// 且其启动成本高，不适合作为按游戏衍生的轻量子进程。
    /// 参数格式与旧版（SAM.Wpf 自宿主）保持一致，输出协议不变。
    /// </summary>
    internal static class Program
    {
        internal static int Main(string[] args)
        {
            foreach (var arg in args)
            {
                // 统计 worker：Steam 的 ISteamUserStats 绑定进程 SteamAppId 上下文，
                // 查看某游戏的成就/统计时以本模式启动隐藏子进程（同旧版按游戏开 SAM.Game.exe）。
                if (arg.StartsWith("--stats-worker=", StringComparison.OrdinalIgnoreCase) == true &&
                    long.TryParse(arg.Substring(15), NumberStyles.Integer, CultureInfo.InvariantCulture, out var workerAppId) == true)
                {
                    return StatsWorker.Run(workerAppId);
                }
            }

            // 家庭共享判别探测：
            // --probe-familysharing=id1,id2 → 主进程上下文诊断（自购/购买时间戳）
            // --probe-appctx=<appId> → 按游戏上下文探测（FamilySharingService 解析其输出）
            foreach (var arg in args)
            {
                if (arg.StartsWith("--probe-familysharing=", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var ids = arg.Substring("--probe-familysharing=".Length)
                        .Split(',', StringSplitOptions.RemoveEmptyEntries)
                        .Select(text => uint.Parse(text, CultureInfo.InvariantCulture))
                        .ToArray();
                    return SteamProbes.RunFamilySharingProbe(ids);
                }

                if (arg.StartsWith("--probe-appctx=", StringComparison.OrdinalIgnoreCase) == true &&
                    uint.TryParse(arg.Substring("--probe-appctx=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ctxAppId) == true)
                {
                    return SteamProbes.RunAppContextProbe(ctxAppId);
                }
            }

            Console.Error.WriteLine(
                "usage: SAM.Worker --stats-worker=<appId> | --probe-appctx=<appId> | --probe-familysharing=<id1,id2,...>");
            return 64;
        }
    }
}
