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

using Wpf.Ui.Abstractions;

namespace SAM.WpfApp.Services
{
    /// <summary>
    /// WPF-UI 导航页提供器（INavigationViewPageProvider）的最小实现：
    /// 显式注册页面工厂 + 实例缓存，不引入 DI 容器。
    /// </summary>
    public sealed class PageService : INavigationViewPageProvider
    {
        private readonly Dictionary<Type, Func<object>> _Factories = new();
        private readonly Dictionary<Type, object> _Cache = new();

        public PageService Register(Type pageType, Func<object> factory)
        {
            this._Factories[pageType] = factory;
            return this;
        }

        public object? GetPage(Type pageType)
        {
            if (this._Cache.TryGetValue(pageType, out var cached) == true)
            {
                return cached;
            }

            if (this._Factories.TryGetValue(pageType, out var factory) == false)
            {
                return null;
            }

            var page = factory();
            this._Cache[pageType] = page;
            return page;
        }
    }
}
