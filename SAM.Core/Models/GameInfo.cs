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

namespace SAM.Core.Models
{
    /// <summary>
    /// 游戏条目（纯数据模型）。
    /// 迁移自 SAM.Picker\GameInfo.cs，去除了 WinForms 视图字段
    /// （ImageIndex / ImageUrl / ListViewItem Item）——图标缓存与列表项属于视图层关注点。
    /// </summary>
    public class GameInfo
    {
        public GameInfo(uint id, string type)
        {
            this.Id = id;
            this.Type = type;
            this.SetName(null);
        }

        public uint Id { get; }

        /// <summary>"normal" / "demo" / "mod" / "junk" / 其他（来自 games.xml 的 type 属性）。</summary>
        public string Type { get; }

        /// <summary>显示名；Steam 未提供时回退为 "App {Id}"（与旧行为一致）。</summary>
        public string Name { get; private set; } = "";

        /// <summary>本地化名（中文，商店接口）；null = 未获取或无本地化。</summary>
        public string? LocalizedName { get; private set; }

        /// <summary>卡片显示名：本地化优先，回退 Steam 原名。</summary>
        public string DisplayName => this.LocalizedName ?? this.Name;

        /// <summary>设置原始名称；null 触发回退。</summary>
        public void SetName(string? name)
        {
            this.Name = name ?? "App " + this.Id.ToString(CultureInfo.InvariantCulture);
        }

        public void SetLocalizedName(string? name)
        {
            this.LocalizedName = name;
        }
    }
}
