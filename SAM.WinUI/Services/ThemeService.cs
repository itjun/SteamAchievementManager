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
        /// 平台背景：Win11 用 Mica（内容保持透明分层）；Win10 无 Mica，
        /// WinUI 3 窗口默认透明会透出桌面，回退为纯色主题背景。
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
                if (root is Microsoft.UI.Xaml.Controls.Panel panel)
                {
                    panel.Background = FindThemeBrush("SolidBackgroundFillColorBaseBrush")
                        ?? FindThemeBrush("ApplicationPageBackgroundThemeBrush")
                        ?? new SolidColorBrush(Microsoft.UI.Colors.LightGray);
                }
            }

            this.UpdateCaptionButtons();
        }

        /// <summary>标题栏按钮颜色（ExtendsContentIntoTitleBar 后不会自动跟随应用主题，需手动指定）。</summary>
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
                titleBar.ButtonBackgroundColor = null;
                titleBar.ButtonHoverBackgroundColor = dark
                    ? Windows.UI.Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)
                    : Windows.UI.Color.FromArgb(0x33, 0x00, 0x00, 0x00);
            }
            catch
            {
                // 旧系统不支持时忽略（按钮维持系统默认配色）。
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
