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

using System.Windows;
using System.Windows.Media;
using SAM.WpfApp.Models;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace SAM.WpfApp.Services
{
    /// <summary>
    /// 主题服务：应用深/浅主题 + Mica 背景 + 跟随系统。
    /// Accent 使用系统强调色（WPF-UI 默认行为）。
    /// </summary>
    public sealed class ThemeService
    {
        private Window? _WatchedWindow;

        public ThemeService()
        {
            // 自定义语义画刷随主题切换（含 SystemThemeWatcher 驱动的变化）。
            ApplicationThemeManager.Changed += (theme, _) => UpdateSemanticBrushes(theme);
        }

        public ThemeMode CurrentMode { get; private set; } = ThemeMode.System;

        /// <summary>家庭共享状态章的三支画刷：深浅主题各自校准文本对比度（App.xaml 放浅色基线）。</summary>
        private static void UpdateSemanticBrushes(ApplicationTheme theme)
        {
            bool dark = theme is ApplicationTheme.Dark or ApplicationTheme.HighContrast;
            var resources = Application.Current.Resources;

            static SolidColorBrush Freeze(Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }

            resources["FamilySharedTextBrush"] = dark
                ? Freeze(Color.FromRgb(0xE8, 0xA3, 0x3D))
                : Freeze(Color.FromRgb(0x9A, 0x5B, 0x14));
            resources["FamilySharedBackgroundBrush"] = dark
                ? Freeze(Color.FromArgb(0x2E, 0xE8, 0xA3, 0x3D))
                : Freeze(Color.FromArgb(0x1F, 0xE8, 0xA3, 0x3D));
            resources["FamilySharedBorderBrush"] = dark
                ? Freeze(Color.FromArgb(0x3D, 0xE8, 0xA3, 0x3D))
                : Freeze(Color.FromArgb(0x33, 0xE8, 0xA3, 0x3D));
        }

        /// <param name="window">主窗口；跟随系统模式时需要它来挂接系统主题监听。</param>
        public void Apply(ThemeMode mode, Window? window = null)
        {
            if (this._WatchedWindow != null)
            {
                SystemThemeWatcher.UnWatch(this._WatchedWindow);
                this._WatchedWindow = null;
            }

            this.CurrentMode = mode;

            switch (mode)
            {
                case ThemeMode.Dark:
                    ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, true);
                    break;

                case ThemeMode.Light:
                    ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, true);
                    break;

                case ThemeMode.System:
                {
                    var appTheme = ApplicationThemeManager.GetSystemTheme() switch
                    {
                        SystemTheme.Light => ApplicationTheme.Light,
                        SystemTheme.Dark => ApplicationTheme.Dark,
                        SystemTheme.HCWhite or SystemTheme.HCBlack or SystemTheme.HC1 or
                        SystemTheme.HC2 or SystemTheme.Glow or SystemTheme.CapturedMotion or
                        SystemTheme.Sunrise or SystemTheme.Flow => ApplicationTheme.HighContrast,
                        _ => ApplicationTheme.Dark,
                    };
                    ApplicationThemeManager.Apply(appTheme, WindowBackdropType.Mica, true);

                    if (window != null)
                    {
                        SystemThemeWatcher.Watch(window, WindowBackdropType.Mica, true);
                        this._WatchedWindow = window;
                    }
                    break;
                }
            }
        }
    }
}
