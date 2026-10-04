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

using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Navigation;
using SAM.WinUIApp.ViewModels;

namespace SAM.WinUIApp.Views.Pages
{
    /// <summary>
    /// 库 ↔ 详情统一内容区：CurrentView 变更 → 子视图 Visibility + DataContext 切换
    ///（代码驱动，绕开 WinUI 中 ContentControl 绑定对后续变更不稳定的问题）。
    /// </summary>
    public sealed partial class GameLibraryPage : Page
    {
        private MainViewModel? _Main;

        public GameLibraryPage()
        {
            this.InitializeComponent();
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            if (e.Parameter is MainViewModel mainViewModel)
            {
                this.DataContext = mainViewModel;
                if (this._Main != null)
                {
                    this._Main.PropertyChanged -= this.OnMainPropertyChanged;
                }

                this._Main = mainViewModel;
                mainViewModel.PropertyChanged += this.OnMainPropertyChanged;
                this.ShowCurrent(mainViewModel.CurrentView);
            }
        }

        protected override void OnNavigatedFrom(NavigationEventArgs e)
        {
            base.OnNavigatedFrom(e);
            if (this._Main != null)
            {
                this._Main.PropertyChanged -= this.OnMainPropertyChanged;
                this._Main = null;
            }
        }

        private void OnMainPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.CurrentView))
            {
                this.ShowCurrent(this._Main?.CurrentView);
            }
        }

        private void ShowCurrent(ViewModelBase? viewModel)
        {
            var detail = viewModel is GameDetailViewModel;
            var active = detail ? (FrameworkElement)this.DetailView : this.LibraryView;
            var inactive = detail ? (FrameworkElement)this.LibraryView : this.DetailView;

            if (active.DataContext != viewModel)
            {
                active.DataContext = viewModel;
            }

            var switchTarget = active.Visibility == Visibility.Collapsed;
            if (switchTarget == true)
            {
                this.PlayEntrance(active);
            }

            active.Visibility = Visibility.Visible;
            inactive.Visibility = Visibility.Collapsed;
        }

        /// <summary>Fluent 入场动效：淡入 + 24px 上移（约 250ms，标准缓动）。
        /// 系统关闭动画（辅助功能"减少动画"）时跳过，尊重无障碍偏好。</summary>
        private void PlayEntrance(UIElement element)
        {
            try
            {
                var uiSettings = new Windows.UI.ViewManagement.UISettings();
                if (uiSettings.AnimationsEnabled == false)
                {
                    return;
                }

                var visual = ElementCompositionPreview.GetElementVisual(element);
                var compositor = visual.Compositor;
                var easing = compositor.CreateCubicBezierEasingFunction(
                    new System.Numerics.Vector2(0.1f, 0.9f),
                    new System.Numerics.Vector2(0.2f, 1.0f));
                var duration = TimeSpan.FromMilliseconds(250);

                visual.Opacity = 0f;
                visual.Offset = new System.Numerics.Vector3(0f, 24f, 0f);

                var fade = compositor.CreateScalarKeyFrameAnimation();
                fade.Duration = duration;
                fade.InsertKeyFrame(1.0f, 1.0f, easing);

                var rise = compositor.CreateVector3KeyFrameAnimation();
                rise.Duration = duration;
                rise.InsertKeyFrame(1.0f, new System.Numerics.Vector3(0f, 0f, 0f), easing);

                visual.StartAnimation("Opacity", fade);
                visual.StartAnimation("Offset", rise);
            }
            catch
            {
                // 动效失败不影响切换本身。
            }
        }
    }
}
