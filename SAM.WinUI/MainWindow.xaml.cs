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

// WASDK 2.x：Backdrop 类型已从 Microsoft.UI.Composition.SystemBackdrops
// 移入 Microsoft.UI.Xaml.Media。
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace SAM.WinUIApp
{
    public sealed partial class MainWindow : Window
    {
        public MainWindow()
        {
            this.InitializeComponent();

            this.Title = "Steam 成就管理器";
            this.ExtendsContentIntoTitleBar = true;

            // Mica 仅 Win11 (build 22000+) 生效；WinUI 3 窗口默认透明，
            // Win10 回退为纯色主题背景（否则整窗透出桌面）。
            if (Environment.OSVersion.Version.Build >= 22000)
            {
                this.SystemBackdrop = new MicaBackdrop();
            }
            else
            {
                this.RootGrid.Background = FindThemeBrush("SolidBackgroundFillColorBaseBrush")
                    ?? FindThemeBrush("ApplicationPageBackgroundThemeBrush")
                    ?? new SolidColorBrush(Microsoft.UI.Colors.LightGray);
            }
        }
        private static Brush? FindThemeBrush(string key)
        {
            if (Application.Current.Resources.TryGetValue(key, out var value) == true &&
                value is Brush brush)
            {
                return brush;
            }

            return null;
        }
    }
}
