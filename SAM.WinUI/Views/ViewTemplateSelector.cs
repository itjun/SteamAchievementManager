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

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SAM.WinUIApp.ViewModels;

namespace SAM.WinUIApp.Views
{
    /// <summary>
    /// 内容视图模板选择器（WinUI 无 WPF 式隐式 DataTemplate，
    /// 库 ↔ 详情切换由 MainViewModel.CurrentView + 本选择器驱动）。
    /// </summary>
    public sealed class ViewTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? LibraryTemplate { get; set; }

        public DataTemplate? DetailTemplate { get; set; }

        protected override DataTemplate? SelectTemplateCore(object item)
        {
            return item switch
            {
                GameLibraryViewModel => this.LibraryTemplate,
                GameDetailViewModel => this.DetailTemplate,
                _ => base.SelectTemplateCore(item),
            };
        }
    }
}
