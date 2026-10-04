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

namespace SAM.WinUIApp.Models
{
    /// <summary>侧栏类型分类（key: all/normal/demo/mod/junk/other）。</summary>
    public sealed record GameCategory(string Key, string Label, int Count);

    /// <summary>所有权筛选（与类型分类、安装状态正交）。</summary>
    public enum OwnershipFilter
    {
        All = 0,
        Owned,
        FamilyShared,
    }

    public sealed record OwnershipChoice(string Label, OwnershipFilter Value);

    /// <summary>安装状态筛选（纯本地库目录扫描结果）。</summary>
    public enum InstallFilter
    {
        All = 0,
        Installed,
        NotInstalled,
    }

    public sealed record InstallChoice(string Label, InstallFilter Value);
}
