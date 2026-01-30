using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Windows.Data;
using CncController.Services;
using CncController.Models;

namespace CncController.ViewModels
{
    public partial class HistoryViewModel : ObservableObject
    {
        // 用於介面綁定的視圖 (支援過濾功能)
        public ICollectionView LogsView { get; }

        // 目前選中的過濾器名稱 (用於控制按鈕狀態)
        [ObservableProperty]
        private string _currentFilter = "ALL";

        public HistoryViewModel()
        {
            // 1. 取得 AlarmService 的原始資料
            var sourceList = AlarmService.Instance.AllLogs;

            // 2. 建立 CollectionView (這是 WPF 內建的強大過濾器)
            LogsView = CollectionViewSource.GetDefaultView(sourceList);

            // 3. 設定過濾邏輯
            LogsView.Filter = FilterLogs;

            // 4. 依照時間排序 (最新的在上面) - 雖然 Insert(0) 已經是最新，但保險起見可加
            // LogsView.SortDescriptions.Add(new SortDescription("Time", ListSortDirection.Descending));
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

            // 這裡不需要 Refresh，因為 HistoryView 是顯示 AllLogs (歷史紀錄)，
            // ClearActiveAlarms 只清 ActiveAlarms，歷史紀錄會多一筆 "Reset" Log，會自動出現。
        }
    }
}