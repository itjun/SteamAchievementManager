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

namespace SAM.Core.Services
{
    /// <summary>
    /// 家庭共享判别探测（诊断用途，逐行输出到 stdout 供父进程或人工解析）。
    /// 宿主为 SAM.Worker 子进程；原先直接放在 UI 进程入口里。
    /// </summary>
    public static class SteamProbes
    {
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern uint SetErrorMode(uint mode);

        /// <summary>探测进程禁用硬错误弹窗（0xC0000005 崩溃时静默退出，不再阻塞等点击）。</summary>
        private static void SuppressCrashDialogs()
        {
            const uint SEM_FAILCRITICALERRORS = 0x0001;
            const uint SEM_NOGPFAULTERRORBOX = 0x0002;
            _ = SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX);
        }

        /// <summary>逐行输出探测结果（stdout 重定向时可见）；每行即刷，崩溃时可定位到最后一次调用。</summary>
        public static int RunFamilySharingProbe(uint[] ids)
        {
            SuppressCrashDialogs();
            var probe = new SteamService();
            var init = probe.Initialize(0);
            if (init.Success == false)
            {
                Console.WriteLine($"init failed: {init.Failure} {init.Detail}");
                return 2;
            }

            var me = probe.GetSteamId();
            Console.WriteLine($"me = {me}");
            foreach (var id in ids)
            {
                Console.WriteLine($"[{id}] subscribed = {probe.IsSubscribedApp(id)}");
                Console.Out.Flush();
                Console.WriteLine($"[{id}] purchaseTime = {probe.GetEarliestPurchaseUnixTime(id)}");
                Console.Out.Flush();
            }

            probe.Dispose();
            return 0;
        }

        /// <summary>
        /// 按游戏上下文探测（SteamAppId=appId 的独立进程）。
        /// 输出格式被 FamilySharingService 解析（ProbeMarker），改动需两边同步。
        /// </summary>
        public static int RunAppContextProbe(uint appId)
        {
            SuppressCrashDialogs();
            Console.WriteLine($"probe starting, appId={appId}");
            Console.Out.Flush();
            var probe = new SteamService();
            var init = probe.Initialize(appId);
            if (init.Success == false)
            {
                Console.WriteLine($"init(app={appId}) failed: {init.Failure} {init.Detail}");
                Console.Out.Flush();
                return 2;
            }

            Console.WriteLine("init ok");
            Console.Out.Flush();
            Console.WriteLine($"me = {probe.GetSteamId()}");
            Console.Out.Flush();
            Console.WriteLine($"[{appId}] subscribed = {probe.IsSubscribedApp(appId)}");
            Console.Out.Flush();
            Console.WriteLine($"[{appId}] familySharing(current app) = {probe.IsFamilySharedCurrentApp()}");
            Console.Out.Flush();
            // GetAppOwner 槽位带参/无参两种调用形状均实测 0xC0000005，已弃用。
            Console.WriteLine($"[{appId}] purchaseTime = {probe.GetEarliestPurchaseUnixTime(appId)}");
            Console.Out.Flush();

            probe.Dispose();
            return 0;
        }
    }
}
