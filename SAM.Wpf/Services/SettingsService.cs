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

using System.IO;
using System.Text.Json;
using SAM.WpfApp.Models;

namespace SAM.WpfApp.Services
{
    /// <summary>
    /// 应用设置持久化（%LOCALAPPDATA%\SAM.Wpf\settings.json）。
    /// 旧版 WinForms 完全没有设置存储；这里是新客户端的最小实现。
    /// </summary>
    public sealed class SettingsService
    {
        private static readonly string SettingsDirectory =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SAM.Wpf");

        private static readonly string SettingsFilePath = Path.Combine(SettingsDirectory, "settings.json");

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
        };

        public AppSettings Settings { get; private set; } = new();

        public SettingsService()
        {
            this.Load();
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
