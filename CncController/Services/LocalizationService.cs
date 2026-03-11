// [2026-03-11] 多語言切換服務：動態替換 ResourceDictionary 實現語言切換
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace CncController.Services
{
    public class LocalizationService : INotifyPropertyChanged
    {
        public static LocalizationService Instance { get; } = new LocalizationService();
        public event PropertyChangedEventHandler? PropertyChanged;

        private string _currentLanguage = "zh-TW";
        public string CurrentLanguage
        {
            get => _currentLanguage;
            private set
            {
                if (_currentLanguage != value)
                {
                    _currentLanguage = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentLanguage)));
                }
            }
        }

        // [2026-03-11] 切換語言：移除舊語言 ResourceDictionary，載入新語言
        public void SwitchLanguage(string culture)
        {
            var app = Application.Current;
            if (app == null) return;

            var langUri = culture switch
            {
                "en-US" => new Uri("Resources/Languages/Lang.en-US.xaml", UriKind.Relative),
                _ => new Uri("Resources/Languages/Lang.zh-TW.xaml", UriKind.Relative)
            };

            // 找到並移除現有語言字典
            var existing = app.Resources.MergedDictionaries
                .FirstOrDefault(d => d.Source?.OriginalString.Contains("Languages/Lang.") == true);
            if (existing != null)
                app.Resources.MergedDictionaries.Remove(existing);

            // 載入新語言字典（DynamicResource 自動更新 UI）
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = langUri });
            CurrentLanguage = culture;
        }

        // 索引器（保留相容性）
        public string this[string key] => Application.Current?.TryFindResource(key) as string ?? key;
    }
}
