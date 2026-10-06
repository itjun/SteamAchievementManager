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
using Microsoft.UI.Xaml.Media;
using SAM.WinUIApp.Models;

namespace SAM.WinUIApp.Services
{
    /// <summary>
    /// 主题服务：深/浅/跟随系统（根元素 RequestedTheme）+ 平台回退背景 + 标题栏按钮配色。
    /// Accent 跟随系统强调色（WinUI 默认行为）。
    /// </summary>
    public sealed class ThemeService
    {
        private readonly Windows.UI.ViewManagement.UISettings _UiSettings = new();

        private Window? _Window;
        private FrameworkElement? _Root;

        public ThemeMode CurrentMode { get; private set; } = ThemeMode.System;

        /// <summary>当前生效的是否深色（跟随系统时读系统值；用于标题栏按钮等手动配色处）。</summary>
        public bool IsEffectiveDark
        {
            get
            {
                var root = this._Root;
                if (root == null)
                {
                    return false;
                }

                if (root.ActualTheme == ElementTheme.Dark)
                {
                    return true;
                }

                if (root.ActualTheme == ElementTheme.Light)
                {
                    return false;
                }

                // Default：交由系统注册表判断。
                using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                var appsUseLight = key?.GetValue("AppsUseLightTheme") as int?;
                return appsUseLight == 0;
            }
        }

        public void Apply(ThemeMode mode, Window? window)
        {
            if (window != null)
            {
                this._Window = window;
                this._Root = window.Content as FrameworkElement;
            }

            this.CurrentMode = mode;
            var root = this._Root;
            if (root == null)
            {
                return;
            }

            root.RequestedTheme = mode switch
            {
                ThemeMode.Dark => ElementTheme.Dark,
                ThemeMode.Light => ElementTheme.Light,
                _ => ElementTheme.Default,
            };

            this.UpdatePlatformBackdrop(root);

            // 跟随系统：系统明暗切换时刷新标题栏按钮等手动配色
            //（RequestedTheme=Default 的 XAML 资源会自动跟随，无需干预）。
            this._UiSettings.ColorValuesChanged -= this.OnSystemColorsChanged;
            if (mode == ThemeMode.System)
            {
                this._UiSettings.ColorValuesChanged += this.OnSystemColorsChanged;
            }
        }

        private void OnSystemColorsChanged(Windows.UI.ViewManagement.UISettings sender, object args)
        {
            var root = this._Root;
            if (root == null)
            {
                return;
            }

            root.DispatcherQueue.TryEnqueue(() =>
            {
                this.UpdateCaptionButtons();
            });
        }

        /// <summary>
        /// 平台背景：Win11 用 Mica（根背景置空让材质贯通）；其余情况由
        /// MainWindow.xaml 的 {ThemeResource SolidBackgroundFillColorBaseBrush} 提供
        /// 主题感知底色——不要在代码里从 Application.Current.Resources 取主题画刷
        ///（那按应用级主题解析，元素级 RequestedTheme 对它无效）。
        /// </summary>
        private void UpdatePlatformBackdrop(FrameworkElement root)
        {
            var window = this._Window;
            if (window == null)
            {
                return;
            }

            if (Environment.OSVersion.Version.Build >= 22000)
            {
                window.SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
                if (root is Microsoft.UI.Xaml.Controls.Panel panel)
                {
                    panel.Background = null;
                }
            }
            else
            {
                window.SystemBackdrop = null;
            }

            this.UpdateCaptionButtons();
        }

        /// <summary>
        /// 标题栏按钮配色（ExtendsContentIntoTitleBar 后系统不跟随应用主题）。
        /// 按钮背景必须对齐"通栏"实际显色——通栏是 NavigationView 内容层
        /// （LayerFillColorDefault 半透明白）叠在底色/材质上，而非纯底色：
        /// Win11 Mica 下给按钮填同款半透明层色（与通栏同样叠在云母上，含壁纸染色完全同色）；
        /// 其余平台直接填合成后的实色（像素实测：浅 #F3F3F3⊕50%白=#F9F9F9、
        /// 深 #202020⊕3%白=#272727），否则按钮条比通栏深一档形成竖直色带。
        /// </summary>
        private void UpdateCaptionButtons()
        {
            var window = this._Window;
            if (window == null)
            {
                return;
            }

            try
            {
                var titleBar = window.AppWindow.TitleBar;
                var dark = this.IsEffectiveDark;
                var foreground = dark ? Windows.UI.Color.FromArgb(255, 255, 255, 255)
                                      : Windows.UI.Color.FromArgb(255, 0, 0, 0);
                titleBar.ButtonForegroundColor = foreground;
                titleBar.ButtonHoverForegroundColor = foreground;
                titleBar.ButtonPressedForegroundColor = foreground;

                var hoverFill = dark
                    ? Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                    : Windows.UI.Color.FromArgb(0x33, 0x00, 0x00, 0x00);
                titleBar.ButtonHoverBackgroundColor = hoverFill;

                if (Environment.OSVersion.Version.Build >= 22000)
                {
                    // Mica：LayerFillColorDefault 同款半透明层（浅 50%白 / 深 3%白），
                    // 叠在云母上与通栏（云母+内容层）完全同色；alpha=0 的纯透明
                    // 反而比通栏少一层，会露出一条未提亮的按钮条。
                    var layer = dark
                        ? Windows.UI.Color.FromArgb(0x08, 0xFF, 0xFF, 0xFF)
                        : Windows.UI.Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF);
                    titleBar.ButtonBackgroundColor = layer;
                    titleBar.ButtonInactiveBackgroundColor = layer;
                }
                else
                {
                    // 无材质平台的通栏合成实色（SolidBackgroundFillColorBase 叠
                    // LayerFillColorDefault 后的实测结果）。
                    var layerColor = dark
                        ? Windows.UI.Color.FromArgb(255, 0x27, 0x27, 0x27)
                        : Windows.UI.Color.FromArgb(255, 0xF9, 0xF9, 0xF9);
                    titleBar.ButtonBackgroundColor = layerColor;
                    titleBar.ButtonInactiveBackgroundColor = layerColor;
                }
            }
            catch
            {
                // 旧系统不支持时忽略（按钮维持系统默认配色）。
            }
        }
    }
}
