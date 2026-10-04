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

using Microsoft.UI.Dispatching;

namespace SAM.WinUIApp
{
    /// <summary>
    /// DispatcherQueue 可等待封装（WASDK 2.5 无内建 EnqueueAsync 扩展）。
    /// 供"须在 UI 线程 await 的操作"（如 BitmapImage.SetSourceAsync）使用——
    /// 同步阻塞等待此类操作会死锁（完成回调需要 UI 线程）。
    /// </summary>
    internal static class DispatcherQueueAsync
    {
        public static Task EnqueueActionAsync(this DispatcherQueue queue, Func<Task> action)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            if (queue.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    completion.TrySetResult();
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                }
            }) == false)
            {
                completion.TrySetException(new InvalidOperationException("DispatcherQueue 已关闭。"));
            }

            return completion.Task;
        }
    }
}
