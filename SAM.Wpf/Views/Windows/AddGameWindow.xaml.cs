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

using System.Globalization;
using Wpf.Ui.Controls;

namespace SAM.WpfApp.Views.Windows
{
    public partial class AddGameWindow : FluentWindow
    {
        public AddGameWindow()
        {
            this.InitializeComponent();
            this.Loaded += (_, _) => this.AppIdTextBox.Focus();
        }

        /// <summary>用户确认的 App ID；取消时为 null。</summary>
        public uint? AppId { get; private set; }

        private void OnOkClick(object sender, System.Windows.RoutedEventArgs e)
        {
            if (uint.TryParse(this.AppIdTextBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId) == false)
            {
                this.InvalidHintText.Visibility = System.Windows.Visibility.Visible;
                this.AppIdTextBox.Focus();
                return;
            }

            this.AppId = appId;
            this.DialogResult = true;
        }

        private void OnCancelClick(object sender, System.Windows.RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}
