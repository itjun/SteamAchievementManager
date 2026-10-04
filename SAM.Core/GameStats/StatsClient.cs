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

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;

namespace SAM.Core.GameStats
{
    /// <summary>
    /// 主进程侧的统计客户端：以 --stats-worker={appId} 启动 SAM.Worker 子进程（隐藏），
    /// 通过 JSON-lines 发送请求。每个游戏一个实例，离开游戏详情时 Dispose。
    /// </summary>
    public sealed class StatsClient : IDisposable
    {
        private static readonly TimeSpan ResponseTimeout = TimeSpan.FromSeconds(45);

        private readonly Process _Process;
        private readonly StreamReader _Reader;
        private readonly StreamWriter _Writer;
        private readonly SemaphoreSlim _Lock = new(1, 1);
        private int _NextId;

        public StatsClient(long appId, string? workerPath = null)
        {
            var exePath = workerPath
                ?? ResolveWorkerPath()
                ?? throw new InvalidOperationException("无法定位统计工作进程可执行文件。");

            var info = new ProcessStartInfo(
                exePath,
                "--stats-worker=" + appId.ToString(CultureInfo.InvariantCulture))
            {
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
            };

            this._Process = Process.Start(info)
                ?? throw new InvalidOperationException("无法启动统计工作进程。");

            this._Process.ErrorDataReceived += (_, _) => { };
            this._Process.BeginErrorReadLine();

            this._Reader = this._Process.StandardOutput;
            this._Writer = this._Process.StandardInput;
        }

        /// <summary>
        /// 解析 worker 可执行文件：首选同目录 SAM.Worker.exe（新架构独立宿主）；
        /// 缺失时回退自身副本（旧自宿主布局，过渡期 SAM.Wpf 仍走此路径）。
        /// </summary>
        private static string? ResolveWorkerPath()
        {
            var candidate = Path.Combine(AppContext.BaseDirectory, "SAM.Worker.exe");
            if (File.Exists(candidate) == true)
            {
                return candidate;
            }

            return Environment.ProcessPath;
        }

        public Task<WorkerResponse> GetAsync(CancellationToken cancellationToken = default)
        {
            return this.SendAsync("get", cancellationToken: cancellationToken);
        }

        public Task<WorkerResponse> StoreAsync(
            List<AchievementChange>? achievements,
            List<StatChange>? stats,
            CancellationToken cancellationToken = default)
        {
            return this.SendAsync("store", achievements, stats, cancellationToken: cancellationToken);
        }

        public Task<WorkerResponse> ResetAllAsync(bool achievementsToo, CancellationToken cancellationToken = default)
        {
            return this.SendAsync("resetAll", achievementsToo: achievementsToo, cancellationToken: cancellationToken);
        }

        private async Task<WorkerResponse> SendAsync(
            string cmd,
            List<AchievementChange>? achievements = null,
            List<StatChange>? stats = null,
            bool achievementsToo = false,
            CancellationToken cancellationToken = default)
        {
            await this._Lock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var request = new WorkerRequest()
                {
                    Id = Interlocked.Increment(ref this._NextId),
                    Cmd = cmd,
                    Achievements = achievements,
                    Stats = stats,
                    AchievementsToo = achievementsToo,
                };

                await this._Writer.WriteLineAsync(
                    JsonSerializer.Serialize(request, ProtocolJson.Options)).ConfigureAwait(false);
                await this._Writer.FlushAsync().ConfigureAwait(false);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(ResponseTimeout);

                while (true)
                {
                    var line = await this._Reader.ReadLineAsync(timeout.Token).ConfigureAwait(false);
                    if (line == null)
                    {
                        throw new IOException("统计工作进程已退出。");
                    }

                    var response = JsonSerializer.Deserialize<WorkerResponse>(line, ProtocolJson.Options);
                    if (response == null)
                    {
                        continue;
                    }

                    if (response.Id != request.Id)
                    {
                        continue;
                    }

                    return response;
                }
            }
            finally
            {
                this._Lock.Release();
            }
        }

        public void Dispose()
        {
            try
            {
                // 关闭 stdin 让子进程自然退出；超时则强杀。
                this._Writer.Dispose();
                if (this._Process.WaitForExit(2000) == false)
                {
                    this._Process.Kill();
                }
            }
            catch
            {
                // 清理阶段忽略。
            }

            this._Process.Dispose();
            this._Lock.Dispose();
        }
    }
}
