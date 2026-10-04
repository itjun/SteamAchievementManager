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

using CommunityToolkit.Mvvm.ComponentModel;
using SAM.Core.GameStats;
using SAM.Core.Services;

namespace SAM.WinUIApp.ViewModels
{
    /// <summary>
    /// 单行统计。EditValue 为编辑文本（NumberBox.Text 绑定，校验沿用
    /// StatisticsService.TryParse 语义）；受保护行不可编辑（旧版由
    /// StatIsProtectedException + OnStatDataError 处理，新 UI 前置禁用）。
    /// </summary>
    public partial class StatisticRowViewModel : ObservableObject
    {
        public StatisticRowViewModel(StatisticData data)
        {
            this.Data = data;
            this.EditValue = StatisticsService.FormatValue(data);
        }

        public StatisticData Data { get; }

        public string Id => this.Data.Id;

        public string DisplayName => this.Data.Name;

        public bool IsProtected => StatisticsService.IsProtected(this.Data);

        /// <summary>Extra 列（IncrementOnly / Protected / UnknownPermission，与旧版一致）。</summary>
        public string Extra => StatisticsService.BuildExtra(this.Data.IsIncrementOnly, this.Data.Permission);

        [ObservableProperty]
        public partial string EditValue { get; set; }

        [ObservableProperty]
        public partial string? ErrorText { get; set; }

        /// <summary>是否允许编辑（详情页的"启用统计编辑"开关 && 非受保护）。</summary>
        [ObservableProperty]
        public partial bool IsEditable { get; set; }

        public bool IsEdited => this.BuildChange() != null;

        partial void OnEditValueChanged(string value) => this.Validate();

        private void Validate()
        {
            if (string.IsNullOrWhiteSpace(this.EditValue) == true)
            {
                this.ErrorText = null;
                return;
            }

            if (this.IsProtected == true)
            {
                this.ErrorText = "受保护的统计，无法修改";
                return;
            }

            this.ErrorText = StatisticsService.TryParse(this.EditValue, this.Data.IsFloat, out _, out _)
                ? null
                : "无效的数值";
        }

        /// <summary>构建提交变更；未修改或非法时返回 null。</summary>
        public StatChange? BuildChange()
        {
            if (this.IsProtected == true || this.ErrorText != null)
            {
                return null;
            }

            if (StatisticsService.TryParse(this.EditValue, this.Data.IsFloat, out var intValue, out var floatValue) == false)
            {
                return null;
            }

            if (this.Data.IsFloat == true)
            {
                if (floatValue == this.Data.FloatValue)
                {
                    return null;
                }
                return new StatChange()
                {
                    Name = this.Data.Id,
                    IsFloat = true,
                    FloatValue = floatValue,
                };
            }

            if (intValue == this.Data.IntValue)
            {
                return null;
            }
            return new StatChange()
            {
                Name = this.Data.Id,
                IsFloat = false,
                IntValue = intValue,
            };
        }
    }
}
