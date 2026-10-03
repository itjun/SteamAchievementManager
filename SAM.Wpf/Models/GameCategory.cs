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
 *    in a product, an acknowledgment in the original software would be
 *    appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

namespace SAM.WpfApp.Models
{
    /// <summary>
    /// 侧栏游戏类型分类（替代原筛选弹窗的四复选框，单选语义）。
    /// Key 取 games.xml 的 type 属性；"all" 汇总全部类型，"other" 归拢
    /// 已知四类（normal/demo/mod/junk）之外的类型（旧版未知类型恒显示）。
    /// </summary>
    public sealed record GameCategory(string Key, string Label, int Count)
    {
        public string DisplayName => this.Count > 0
            ? $"{this.Label} ({this.Count})"
            : this.Label;
    }
}
