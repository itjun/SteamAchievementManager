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

using System.Globalization;
using System.Text.RegularExpressions;

namespace SAM.Core.Services
{
    /// <summary>
    /// 本地已安装（已下载）游戏扫描。两个数据源取并集（纯文件读取，不触
    /// steamclient 接口——规避 vtable 签名风险，也不产生任何会让 Steam 标记
    /// "App Running" 的副作用）：
    ///
    /// 1. steamapps\libraryfolders.vdf：每个库目录（含其他盘的库）的 "apps" 段
    ///    列出该库有文件的 AppId。缺点：Steam 懒更新——刚安装完的游戏可能还没
    ///    被写进该列表（实测 992300 更新完安装后 vdf 未同步）。
    /// 2. 各库 steamapps\appmanifest_*.acf：文件名即 AppId，内容 StateFlags 第 2 位
    ///    （值 4）= Fully Installed。这是最及时的权威信号。
    /// </summary>
    public sealed class InstalledGamesService
    {
        /// <summary>"appid" "size" 形状（数字键 + 数字值），只命中 apps 段内条目。</summary>
        private static readonly Regex AppEntry = new("\"(\\d+)\"\\s+\"(\\d+)\"", RegexOptions.Compiled);

        /// <summary>库目录路径行："path" "C:\\..."（vdf 内为转义反斜杠）。</summary>
        private static readonly Regex LibraryPath = new("\"path\"\\s+\"([^\"]+)\"", RegexOptions.Compiled);

        private static readonly Regex StateFlags = new("\"StateFlags\"\\s+\"(\\d+)\"", RegexOptions.Compiled);

        /// <summary>返回本机所有库目录中已安装游戏的 AppId 集合；读取失败返回空集。</summary>
        public HashSet<uint> ScanInstalledAppIds()
        {
            var result = new HashSet<uint>();

            string? steamPath;
            try
            {
                steamPath = API.Steam.GetInstallPath();
            }
            catch
            {
                steamPath = null;
            }

            if (string.IsNullOrEmpty(steamPath) == true)
            {
                return result;
            }

            var libraries = new List<string>() { steamPath };
            var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
            try
            {
                if (File.Exists(vdfPath) == true)
                {
                    var vdf = File.ReadAllText(vdfPath);

                    // 源 1：apps 段条目。
                    foreach (Match match in AppEntry.Matches(vdf))
                    {
                        if (uint.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId) == true)
                        {
                            result.Add(appId);
                        }
                    }

                    // 其余库目录（其他盘）。
                    foreach (Match match in LibraryPath.Matches(vdf))
                    {
                        var path = match.Groups[1].Value.Replace("\\\\", "\\");
                        if (string.Equals(path, steamPath, StringComparison.OrdinalIgnoreCase) == false)
                        {
                            libraries.Add(path);
                        }
                    }
                }
            }
            catch
            {
                // vdf 损坏/占用：继续走 appmanifest 源。
            }

            // 源 2：各库的 appmanifest_*.acf（文件名即 AppId；StateFlags 含 Fully Installed 位）。
            foreach (var library in libraries)
            {
                var manifests = Directory.GetFiles(Path.Combine(library, "steamapps"), "appmanifest_*.acf");
                foreach (var manifest in manifests)
                {
                    var name = Path.GetFileNameWithoutExtension(manifest);
                    if (uint.TryParse(name.AsSpan("appmanifest_".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId) == false)
                    {
                        continue;
                    }

                    if (IsFullyInstalled(manifest) == true)
                    {
                        result.Add(appId);
                    }
                }
            }

            return result;
        }

        private static bool IsFullyInstalled(string manifestPath)
        {
            try
            {
                var match = StateFlags.Match(File.ReadAllText(manifestPath));
                return match.Success == true &&
                    (int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) & 4) != 0;
            }
            catch
            {
                // 读不了内容的清单按文件存在即已安装处理。
                return true;
            }
        }
    }
}
