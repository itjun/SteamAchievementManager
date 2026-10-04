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
using Microsoft.UI.Xaml.Controls;
using Velopack;

namespace SAM.WinUIApp.Services
{
    /// <summary>
    /// 更新对话框：可选更新（可"稍后"）与强制更新（拦截 Closing 不可关，
    /// 仅"立即更新"或"退出应用"）。点更新后对话框保持打开，切确定值进度条，
    /// 下载完成即清理并应用重启。
    /// </summary>
    public static class UpdateDialog
    {
        /// <summary>
        /// 显示更新对话框；另一 ContentDialog 尚在打开（WinUI 单对话框限制，
        /// 如启动初始化失败提示）时重试等待。
        /// </summary>
        public static async Task ShowWithRetryAsync(UpdateService updates, UpdateCheckResult result)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    await ShowAsync(updates, result);
                    return;
                }
                catch (InvalidOperationException) when (attempt < 3)
                {
                    await Task.Delay(TimeSpan.FromSeconds(4));
                }
            }
        }

        public static async Task ShowAsync(UpdateService updates, UpdateCheckResult result)
        {
            if (result.Update == null)
            {
                return;
            }

            var allowClose = false;
            var isBusy = false;

            var headline = new TextBlock()
            {
                Text = $"新版本 {result.NewVersion} 可用（当前 {UpdateService.CurrentVersion}）",
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
            };

            var promptPanel = new StackPanel() { Spacing = 10 };
            promptPanel.Children.Add(headline);

            if (result.IsRequired == true)
            {
                promptPanel.Children.Add(new TextBlock()
                {
                    Text = "此版本必须更新后才能继续使用本应用。",
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            if (string.IsNullOrEmpty(result.Notes) == false)
            {
                promptPanel.Children.Add(new ScrollViewer()
                {
                    MaxHeight = 220,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock()
                    {
                        Text = result.Notes,
                        Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
                        TextWrapping = TextWrapping.Wrap,
                    },
                });
            }

            var progress = new ProgressBar()
            {
                Minimum = 0,
                Maximum = 100,
                Visibility = Visibility.Collapsed,
            };
            var statusText = new TextBlock()
            {
                Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed,
            };
            var progressPanel = new StackPanel()
            {
                Spacing = 8,
                Visibility = Visibility.Collapsed,
                Children = { statusText, progress },
            };

            var dialog = new ContentDialog()
            {
                Title = "软件更新",
                Content = new StackPanel()
                {
                    Spacing = 12,
                    MinWidth = 360,
                    Children = { promptPanel, progressPanel },
                },
                PrimaryButtonText = "立即更新",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = App.MainHost?.Content?.XamlRoot,
            };

            if (result.IsRequired == true)
            {
                // 强制更新：Esc/关闭均取消，只有更新或退出两条路。
                dialog.SecondaryButtonText = "退出应用";
                dialog.Closing += (_, args) =>
                {
                    if (allowClose == false)
                    {
                        args.Cancel = true;
                    }
                };
            }
            else
            {
                dialog.CloseButtonText = "稍后";
            }

            dialog.PrimaryButtonClick += async (sender, args) =>
            {
                if (isBusy == true)
                {
                    args.Cancel = true;
                    return;
                }

                // 保持对话框打开进入下载态（默认点按钮即关闭）。
                args.Cancel = true;
                isBusy = true;
                sender.IsPrimaryButtonEnabled = false;
                promptPanel.Visibility = Visibility.Collapsed;
                progressPanel.Visibility = Visibility.Visible;
                statusText.Visibility = Visibility.Visible;
                statusText.Text = "正在下载更新…";
                progress.Visibility = Visibility.Visible;

                try
                {
                    // Progress<T> 在 UI 线程构造，Velopack 回调自动封送回 UI 线程。
                    var reporter = new Progress<int>(percent => progress.Value = percent);
                    await updates.DownloadAsync(result.Update, reporter);
                    statusText.Text = "下载完成，正在应用更新并重启…";
                    (Application.Current as App)?.PrepareForUpdateExit();
                    updates.ApplyAndRestart(result.Update);

                    // 正常不应到达此处（ApplyUpdatesAndRestart 内部退出进程）。
                    statusText.Text = "更新已就绪，将在下次启动时生效。";
                    allowClose = true;
                    sender.Hide();
                }
                catch (Exception e)
                {
                    isBusy = false;
                    sender.IsPrimaryButtonEnabled = true;
                    statusText.Text = $"更新失败：{e.Message}\n可点击“立即更新”重试。";
                }
            };

            dialog.SecondaryButtonClick += (_, _) =>
            {
                // 强制更新选择退出：统一清理（Worker 子进程/Steam 互操作）后退出。
                allowClose = true;
                (Application.Current as App)?.PrepareForUpdateExit();
                Application.Current.Exit();
            };

            await dialog.ShowAsync();
        }
    }
}
