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

using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SAM.WinUIApp.Models;
using SAM.WinUIApp.Services;

namespace SAM.WinUIApp.ViewModels
{
    public sealed class ThemeOption
    {
        public ThemeOption(ThemeMode mode, string label)
        {
            this.Mode = mode;
            this.Label = label;
        }

        public ThemeMode Mode { get; }

        public string Label { get; }
    }

    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly SettingsService _SettingsService;
        private readonly ThemeService _ThemeService;
        private readonly UpdateService _UpdateService;

        public IReadOnlyList<ThemeOption> ThemeOptions { get; } = new[]
        {
            new ThemeOption(ThemeMode.System, "跟随系统"),
            new ThemeOption(ThemeMode.Light, "浅色"),
            new ThemeOption(ThemeMode.Dark, "深色"),
        };

        [ObservableProperty]
        public partial ThemeOption? SelectedThemeOption { get; set; }

        public string VersionLine =>
            $"版本 {UpdateService.CurrentVersion} · 基于 .NET 8 + WinUI 3 / Windows App SDK（Fluent 2）";

        [ObservableProperty]
        public partial bool IsCheckingUpdate { get; set; }

        [ObservableProperty]
        public partial string UpdateStatusText { get; set; } = "";

        [ObservableProperty]
        public partial string LastCheckText { get; set; } = "";

        [ObservableProperty]
        public partial bool AutoCheckUpdate { get; set; }

        public SettingsViewModel(SettingsService settingsService, ThemeService themeService, UpdateService updateService)
        {
            this._SettingsService = settingsService;
            this._ThemeService = themeService;
            this._UpdateService = updateService;

            this.SelectedThemeOption = this.ThemeOptions.FirstOrDefault(
                option => option.Mode == settingsService.Settings.Theme) ?? this.ThemeOptions[0];
            this.AutoCheckUpdate = settingsService.Settings.AutoCheckUpdate;
            this.LastCheckText = FormatLastCheck(settingsService.Settings.LastUpdateCheckUtc);
        }

        [RelayCommand]
        private async Task CheckUpdateAsync()
        {
            if (this.IsCheckingUpdate == true)
            {
                return;
            }

            this.IsCheckingUpdate = true;
            this.UpdateStatusText = "正在检查更新…";
            try
            {
                var result = await this._UpdateService.CheckAsync();
                this._SettingsService.Settings.LastUpdateCheckUtc = DateTime.UtcNow;
                this._SettingsService.Save();
                this.LastCheckText = FormatLastCheck(this._SettingsService.Settings.LastUpdateCheckUtc);

                switch (result.Status)
                {
                    case UpdateStatus.Available:
                        this.UpdateStatusText = $"已发现新版本 {result.NewVersion}。";
                        await UpdateDialog.ShowWithRetryAsync(this._UpdateService, result);
                        break;
                    case UpdateStatus.NotInstalled:
                        this.UpdateStatusText = "当前为开发模式运行（未从发布包启动），无法检查更新。";
                        break;
                    case UpdateStatus.Failed:
                        this.UpdateStatusText = $"检查更新失败：{result.ErrorMessage}";
                        break;
                    default:
                        this.UpdateStatusText = $"已是最新版本（{UpdateService.CurrentVersion}）。";
                        break;
                }
            }
            finally
            {
                this.IsCheckingUpdate = false;
            }
        }

        partial void OnAutoCheckUpdateChanged(bool value)
        {
            this._SettingsService.Settings.AutoCheckUpdate = value;
            this._SettingsService.Save();
        }

        private static string FormatLastCheck(DateTime? utc) => utc == null
            ? "尚未检查过更新"
            : $"上次检查：{utc.Value.ToLocalTime():yyyy-MM-dd HH:mm}";

        /// <summary>设置页分段控件绑定：选中段返回 true；置 true 切换主题（互斥由 RadioButton 分组保证）。</summary>
        public bool IsSystemTheme
        {
            get => this.SelectedThemeOption?.Mode == ThemeMode.System;
            set { if (value == true) { this.SelectedThemeOption = this.ThemeOptions[0]; } }
        }

        public bool IsLightTheme
        {
            get => this.SelectedThemeOption?.Mode == ThemeMode.Light;
            set { if (value == true) { this.SelectedThemeOption = this.ThemeOptions[1]; } }
        }

        public bool IsDarkTheme
        {
            get => this.SelectedThemeOption?.Mode == ThemeMode.Dark;
            set { if (value == true) { this.SelectedThemeOption = this.ThemeOptions[2]; } }
        }

        partial void OnSelectedThemeOptionChanged(ThemeOption value)
        {
            if (value == null)
            {
                return;
            }

            this.OnPropertyChanged(nameof(this.IsSystemTheme));
            this.OnPropertyChanged(nameof(this.IsLightTheme));
            this.OnPropertyChanged(nameof(this.IsDarkTheme));

            this._ThemeService.Apply(value.Mode, App.MainHost);
            this._SettingsService.Settings.Theme = value.Mode;
            this._SettingsService.Save();
        }
    }
}
