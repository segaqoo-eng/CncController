// [2026-03-06] CachedContentControl：快取已建立的 View，避免每次切換主分頁都重建 XAML 樹
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace CncController.Views
{
    public class CachedContentControl : ContentControl
    {
        private readonly Dictionary<Type, ContentPresenter> _cache = new();
        private readonly Grid _container = new();

        public CachedContentControl()
        {
            // [2026-03-06] 使用 Grid 容器管理快取的 View
            base.Content = _container;
        }

        // [2026-03-06] 攔截 Content 變更（ViewModel 切換），從快取取出或新建 View
        protected override void OnContentChanged(object oldContent, object newContent)
        {
            // 避免攔截我們自己設定的 _container
            if (newContent is Grid g && ReferenceEquals(g, _container))
            {
                base.OnContentChanged(oldContent, newContent);
                return;
            }

            if (newContent == null) return;

            var key = newContent.GetType();

            // 隱藏所有已快取的 View
            foreach (UIElement child in _container.Children)
                child.Visibility = Visibility.Collapsed;

            if (_cache.TryGetValue(key, out var cached))
            {
                // [2026-03-06] 命中快取，直接顯示（不重建 XAML）
                cached.Content = newContent;
                cached.Visibility = Visibility.Visible;
            }
            else
            {
                // [2026-03-06] 首次訪問，透過 DataTemplate 建立 View 並快取
                var template = FindTemplateForType(key);
                var presenter = new ContentPresenter
                {
                    Content = newContent,
                    ContentTemplate = template
                };
                _cache[key] = presenter;
                _container.Children.Add(presenter);
            }
        }

        // [2026-03-06] 從資源字典查找對應 ViewModel 的 DataTemplate
        private DataTemplate FindTemplateForType(Type type)
        {
            var templateKey = new DataTemplateKey(type);
            return TryFindResource(templateKey) as DataTemplate;
        }
    }
}
