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
    /// <summary>安装状态筛选（与所有权/类型筛选正交）：全部 / 已下载 / 未下载。</summary>
    public enum InstallFilter
    {
        All = 0,
        Installed = 1,
        NotInstalled = 2,
    }

    /// <summary>ComboBox 选项（DisplayMemberPath=Label, SelectedValuePath=Value）。</summary>
    public sealed record InstallChoice(string Label, InstallFilter Value);
}
