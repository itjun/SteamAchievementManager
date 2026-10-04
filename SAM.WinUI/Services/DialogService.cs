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

using Microsoft.UI.Xaml.Controls;

namespace SAM.WinUIApp.Services
{
    /// <summary>
    /// 对话框服务：统一 ContentDialog 外观（替代 WPF 版散落各处的 MessageBox，
    /// AddGameWindow 并入为 PromptForAppIdAsync）。
    /// XamlRoot 取自主窗口（单窗口应用）。
    /// </summary>
    public sealed class DialogService
    {
        /// <summary>手动添加游戏：输入 App ID（NumberBox 校验范围），取消返回 null。</summary>
        public async Task<uint?> PromptForAppIdAsync()
        {
            var input = new NumberBox()
            {
                Header = "App ID",
                PlaceholderText = "例如 440",
                Minimum = 1,
                Maximum = uint.MaxValue,
                SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
                Value = 0,
            };

            var dialog = new ContentDialog()
            {
                Title = "添加游戏",
                Content = new StackPanel()
                {
                    Spacing = 8,
                    Children =
                    {
                        new TextBlock()
                        {
                            Text = "输入你拥有的游戏 App ID（Steam 商店页地址里的数字）。",
                            TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                        },
                        input,
                    },
                },
                PrimaryButtonText = "添加",
                CloseButtonText = "取消",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = App.MainHost?.Content?.XamlRoot,
            };

            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return null;
            }

            if (input.Value < 1 || input.Value > uint.MaxValue)
            {
                await this.ShowErrorAsync("Steam 成就管理器", "App ID 无效。");
                return null;
            }

            return (uint)input.Value;
        }

        public async Task ShowErrorAsync(string title, string message)
        {
            var dialog = this.Create(title, message, closeText: "确定");
            await dialog.ShowAsync();
        }

        public async Task ShowInfoAsync(string title, string message)
        {
            var dialog = this.Create(title, message, closeText: "确定");
            await dialog.ShowAsync();
        }

        /// <summary>返回 true=用户选择确认。</summary>
        public async Task<bool> ConfirmAsync(string title, string message, string confirmText = "确定")
        {
            var dialog = this.Create(title, message, confirmText, cancelText: "取消");
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }

        private ContentDialog Create(string title, string message, string closeText, string? cancelText = null)
        {
            var dialog = new ContentDialog()
            {
                Title = title,
                Content = message,
                CloseButtonText = cancelText ?? closeText,
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = App.MainHost?.Content?.XamlRoot,
            };

            if (cancelText != null)
            {
                dialog.PrimaryButtonText = closeText;
            }

            return dialog;
        }
    }
}
