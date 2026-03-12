// [2026-03-12] 主題切換服務：動態替換 Theme ResourceDictionary
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace CncController.Services
{
    public class ThemeService : INotifyPropertyChanged
    {
        public static ThemeService Instance { get; } = new ThemeService();
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _currentTheme = "Default";
        public string CurrentTheme
        {
            get => _currentTheme;
            private set
            {
                if (_currentTheme != value)
                {
                    _currentTheme = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentTheme)));
                }
            }
        }

        // [2026-03-12] 切換主題：移除舊 Theme ResourceDictionary，載入新主題
        public void SwitchTheme(string themeName)
        {
            var app = Application.Current;
            if (app == null) return;

            var themeUri = themeName switch
            {
                "Industrial" => new Uri("Resources/Themes/Theme.Industrial.xaml", UriKind.Relative),
                "Cyber" => new Uri("Resources/Themes/Theme.Cyber.xaml", UriKind.Relative),
                _ => new Uri("Resources/Themes/Theme.Dark.xaml", UriKind.Relative)
            };

            // 找到並移除現有主題字典
            var existing = app.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source?.OriginalString.Contains("Themes/Theme.") == true);
            if (existing != null)
                app.Resources.MergedDictionaries.Remove(existing);

            // 載入新主題字典（DynamicResource 自動更新 UI）
            app.Resources.MergedDictionaries.Insert(0, new ResourceDictionary { Source = themeUri });
            CurrentTheme = themeName;
        }
    }
}
