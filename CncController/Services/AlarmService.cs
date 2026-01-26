using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CncController.Services
{
    /// <summary>
    /// 單一日誌項目模型
    /// </summary>
    public class AlarmLog
    {
        public DateTime Time { get; set; }
        public string Type { get; set; }    // ALARM, ERROR, LOGIN, API
        public string Message { get; set; }
    }

    /// <summary>
    /// 集中式履歷管理服務。
    /// 負責處理：警示、異常、登入、以及 API 請求紀錄。
    /// </summary>
    public class AlarmService : ObservableObject
    {
        private static AlarmService _instance;
        public static AlarmService Instance => _instance ??= new AlarmService();

        // 綁定至 HistoryView 的 DataGrid
        public ObservableCollection<AlarmLog> AllLogs { get; private set; } = new();

        // API 紀錄保留上限
        private const int MAX_API_LOGS = 20;

        /// <summary>
        /// 新增一筆履歷，並確保執行緒安全與筆數限制邏輯。
        /// </summary>
        public void AddLog(string type, string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 針對 API 類型執行筆數限制 (需求 1.4)
                if (type == "API")
                {
                    var apiLogs = AllLogs.Where(x => x.Type == "API").ToList();
                    if (apiLogs.Count >= MAX_API_LOGS)
                    {
                        var oldestApi = apiLogs.LastOrDefault();
                        if (oldestApi != null) AllLogs.Remove(oldestApi);
                    }
                }

                // 插入至最上方 (索引 0)，確保最新資料在最前面
                AllLogs.Insert(0, new AlarmLog
                {
                    Time = DateTime.Now,
                    Type = type.ToUpper(),
                    Message = message
                });
            });
        }
    }
}