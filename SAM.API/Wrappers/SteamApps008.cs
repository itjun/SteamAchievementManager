/* Copyright (c) 2024 Rick (rick 'at' gibbed 'dot' us)
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
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

using System;
using System.Runtime.InteropServices;
using SAM.API.Interfaces;

namespace SAM.API.Wrappers
{
    public class SteamApps008 : NativeWrapper<ISteamApps008>
    {
        #region IsSubscribed
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool NativeIsSubscribedApp(IntPtr self, uint gameId);

        public bool IsSubscribedApp(uint gameId)
        {
            return this.Call<bool, NativeIsSubscribedApp>(this.Functions.IsSubscribedApp, this.ObjectAddress, gameId);
        }
        #endregion

        #region GetCurrentGameLanguage
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate IntPtr NativeGetCurrentGameLanguage(IntPtr self);

        public string GetCurrentGameLanguage()
        {
            var languagePointer = this.Call<IntPtr, NativeGetCurrentGameLanguage>(
                this.Functions.GetCurrentGameLanguage,
                this.ObjectAddress);
            return NativeStrings.PointerToString(languagePointer);
        }
        #endregion

        // 家庭共享判别（2026-10-04 实测定案）：
        // - vtable 恰好 28 槽（0..27），2023+ 无尾部追加；
        // - IsSubscribedFromFamilySharing 是"当前进程 SteamAppId 上下文"的无参方法
        //   ——按 (appId) 带参调用不会崩但恒返回假值（勿用！）；
        // - GetAppOwner 槽位带参/无参两种形状均 0xC0000005，不可调用。
        // 因此主进程无法按 appId 查询家庭共享；唯一途径 = 以目标 appId 初始化的
        // 子进程里调无参版（App.RunAppContextProbe / FamilySharingService）。
        #region FamilySharing
        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        [return: MarshalAs(UnmanagedType.I1)]
        private delegate bool NativeIsFamilySharedCurrentApp(IntPtr self);

        public bool IsFamilySharedCurrentApp()
        {
            return this.Call<bool, NativeIsFamilySharedCurrentApp>(
                this.Functions.IsSubscribedFromFamilySharing, this.ObjectAddress);
        }

        [UnmanagedFunctionPointer(CallingConvention.ThisCall)]
        private delegate uint NativeGetEarliestPurchaseUnixTime(IntPtr self, uint gameId);

        public uint GetEarliestPurchaseUnixTime(uint gameId)
        {
            return this.Call<uint, NativeGetEarliestPurchaseUnixTime>(
                this.Functions.GetEarliestPurchaseUnixTime, this.ObjectAddress, gameId);
        }
        #endregion
    }
}
