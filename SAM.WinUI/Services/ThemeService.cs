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
        /// Win11 Mica 下保持透明让材质贯通；其余平台给按钮背景填主题底色，
        /// 否则系统按默认浅色画非客户区（深色模式下出现白色按钮条）。
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
                    // Mica：保持透明，材质贯通标题栏。
                    titleBar.ButtonBackgroundColor = null;
                    titleBar.ButtonInactiveBackgroundColor = null;
                }
                else
                {
                    // 主题底色（SolidBackgroundFillColorBase：深 #202020 / 浅 #F3F3F3）。
                    var baseColor = dark
                        ? Windows.UI.Color.FromArgb(255, 0x20, 0x20, 0x20)
                        : Windows.UI.Color.FromArgb(255, 0xF3, 0xF3, 0xF3);
                    titleBar.ButtonBackgroundColor = baseColor;
                    titleBar.ButtonInactiveBackgroundColor = baseColor;
                }
            }
            catch
            {
                // 旧系统不支持时忽略（按钮维持系统默认配色）。
            }
        }
    }
}
