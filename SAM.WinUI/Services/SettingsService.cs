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

using System.IO;
using System.Text.Json;
using SAM.WinUIApp.Models;

namespace SAM.WinUIApp.Services
{
    /// <summary>
    /// 应用设置持久化（%LOCALAPPDATA%\SAM\settings.json）。
    /// 首次运行时从旧 SAM.Wpf 目录一次性迁移缓存与设置：
    /// familysharing.json 的探测结果有触发 Steam 批量更新检查的副作用成本，不能丢。
    /// </summary>
    public sealed class SettingsService
    {
        private static readonly string SettingsDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SAM");

        private static readonly string OldDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SAM.Wpf");

        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
        };

        public AppSettings Settings { get; private set; } = new();

        public SettingsService()
        {
            this.MigrateFromLegacy();
            this.Load();
        }

        /// <summary>一次性迁移：目标不存在而旧版存在则拷贝（familysharing.json / settings.json）。</summary>
        private void MigrateFromLegacy()
        {
            try
            {
                foreach (var name in new[] { "familysharing.json", "settings.json" })
                {
                    var source = Path.Combine(OldDirectory, name);
                    var target = Path.Combine(SettingsDirectory, name);
                    if (File.Exists(source) == true && File.Exists(target) == false)
                    {
                        Directory.CreateDirectory(SettingsDirectory);
                        File.Copy(source, target);
                    }
                }
            }
            catch
            {
                // 迁移失败不阻断启动（视为全新安装）。
            }
        }

        public void Load()
        {
            try
            {
                if (File.Exists(SettingsFilePath) == true)
                {
                    this.Settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsFilePath)) ?? new();
                }
            }
            catch
            {
                // 设置损坏时回退默认值，不阻断启动。
                this.Settings = new();
            }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(SettingsDirectory);
                File.WriteAllText(SettingsFilePath, JsonSerializer.Serialize(this.Settings, JsonOptions));
            }
            catch
            {
                // 保存失败不影响运行。
            }
        }
    }
}
