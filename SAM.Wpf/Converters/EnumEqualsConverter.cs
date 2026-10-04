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

using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SAM.WpfApp.Converters
{
    /// <summary>
    /// 枚举值 ↔ RadioButton.IsChecked：parameter 用枚举名（如 "Owned"）。
    /// 取消勾选回传 Binding.DoNothing，避免同组互斥时反向清空源值。
    /// </summary>
    public sealed class EnumEqualsConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value?.ToString() == parameter?.ToString();
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is true &&
                parameter is string name &&
                Enum.TryParse(targetType, name, out var result) == true)
            {
                return result;
            }

            return Binding.DoNothing;
        }
    }
}
