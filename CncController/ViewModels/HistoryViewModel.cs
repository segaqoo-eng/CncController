using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using CncController.Services;
using CncController.Models;

namespace CncController.ViewModels
{
    // [2026-03-12] 報警統計項目（供 STATS 分頁）
    public partial class AlarmStatItem : ObservableObject
    {
        [ObservableProperty] private string _message = "";
        [ObservableProperty] private string _type = "";
        [ObservableProperty] private int _count;
        [ObservableProperty] private string _lastTime = "";
    }

    public partial class HistoryViewModel : ObservableObject
    {
        // 用於介面綁定的視圖 (支援過濾功能)
        public ICollectionView LogsView { get; }

        // 目前選中的過濾器名稱 (用於控制按鈕狀態)
        [ObservableProperty]
        private string _currentFilter = "ALL";

        // [2026-03-12] 報警統計
        [ObservableProperty] private int _totalErrorCount;
        [ObservableProperty] private int _totalWarnCount;
        [ObservableProperty] private int _totalInfoCount;
        [ObservableProperty] private ObservableCollection<AlarmStatItem> _topAlarms = new();
        [ObservableProperty] private string _selectedTab = "LOG";

        public HistoryViewModel()
        {
            // 1. 取得 AlarmService 的原始資料
            var sourceList = AlarmService.Instance.AllLogs;

            // 2. 建立 CollectionView (這是 WPF 內建的強大過濾器)
            LogsView = CollectionViewSource.GetDefaultView(sourceList);

            // 3. 設定過濾邏輯
            LogsView.Filter = FilterLogs;
        }

        // 過濾邏輯核心
        private bool FilterLogs(object item)
        {
            if (item is not AlarmLog log) return false;

            if (CurrentFilter == "ALL") return true;

            // 字串轉 Enum 比對
            return CurrentFilter switch
            {
                "INFO" => log.Type == LogType.Info,
                "WARN" => log.Type == LogType.Warning,
                "ERROR" => log.Type == LogType.Error,
                "DEBUG" => log.Type == LogType.Debug,
                _ => true
            };
        }

        [RelayCommand]
        private void SetFilter(string filterType)
        {
            CurrentFilter = filterType;
            // 通知 View 重新整理過濾結果
            LogsView.Refresh();
        }

        [RelayCommand]
        private void ClearActiveAlarms()
        {
            // 呼叫 Service 清除抬頭警報
            AlarmService.Instance.ClearActiveAlarms();
        }

        // [2026-03-12] 計算報警統計
        [RelayCommand]
        private void RefreshStats()
        {
            var logs = AlarmService.Instance.AllLogs;

            TotalErrorCount = logs.Count(l => l.Type == LogType.Error);
            TotalWarnCount = logs.Count(l => l.Type == LogType.Warning);
            TotalInfoCount = logs.Count(l => l.Type == LogType.Info);

            // Top 10 最頻繁的錯誤/警告訊息
            var grouped = logs
                .Where(l => l.Type == LogType.Error || l.Type == LogType.Warning)
                .GroupBy(l => l.DisplayMessage)
                .OrderByDescending(g => g.Count())
                .Take(10)
                .Select(g => new AlarmStatItem
                {
                    Message = g.Key,
                    Type = g.First().Type.ToString(),
                    Count = g.Count(),
                    LastTime = g.Max(l => l.Time).ToString("MM/dd HH:mm")
                });

            TopAlarms.Clear();
            foreach (var item in grouped)
                TopAlarms.Add(item);
        }
    }
}