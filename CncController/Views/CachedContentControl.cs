// [2026-03-06] CachedContentControl：快取已建立的 View，避免每次切換主分頁都重建 XAML 樹
// [2026-03-11] 修正快取失效：改用自訂 DependencyProperty，避免覆蓋 base.Content（_container Grid）
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace CncController.Views
{
    public class CachedContentControl : ContentControl
    {
        // [2026-03-11] 自訂 DP：ViewModel 綁定至此，不干擾 ContentControl.Content
        public static readonly DependencyProperty CurrentContentProperty =
            DependencyProperty.Register(
                nameof(CurrentContent),
                typeof(object),
                typeof(CachedContentControl),
                new PropertyMetadata(null, OnCurrentContentChanged));

        public object CurrentContent
        {
            get => GetValue(CurrentContentProperty);
            set => SetValue(CurrentContentProperty, value);
        }

        private readonly Dictionary<Type, ContentPresenter> _cache = new();
        private readonly Grid _container = new();

        public CachedContentControl()
        {
            // [2026-03-11] _container 永遠作為 base.Content，不會被綁定覆蓋
            Content = _container;
        }

        private static void OnCurrentContentChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((CachedContentControl)d).SwitchView(e.NewValue);
        }

        // [2026-03-11] 切換 View：從快取取出或首次建立，O(1) 切換
        private void SwitchView(object newContent)
        {
            if (newContent == null) return;

            var key = newContent.GetType();

            // 隱藏所有已快取的 View
            foreach (UIElement child in _container.Children)
                child.Visibility = Visibility.Collapsed;

            if (_cache.TryGetValue(key, out var cached))
            {
                // 命中快取，直接顯示（不重建 XAML 樹）
                cached.Visibility = Visibility.Visible;
            }
            else
            {
                // 首次訪問，透過 DataTemplate 建立 View 並快取
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
