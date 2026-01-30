using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Media; // [重要] 需引用 PresentationCore 才能使用 Brushes
using CommunityToolkit.Mvvm.ComponentModel;
using CncController.Models;

namespace CncController.Services
{
    /// <summary>
    /// [修改] 單一日誌項目模型
    /// 增加 MessageKey 支援多語系，並提供 ColorBrush 供 UI 綁定
    /// </summary>
    public class AlarmLog
    {
        public DateTime Time { get; set; }
        public LogType Type { get; set; }
        public string MessageKey { get; set; } // 存 Key 或 原始訊息

        // UI 顯示用：嘗試翻譯，若無翻譯則顯示原始字串
        public string DisplayMessage
        {
            get
            {
                if (string.IsNullOrEmpty(MessageKey)) return "";
                // 簡單判斷：如果有空白通常是句子，不翻譯；否則嘗試找資源
                if (MessageKey.Contains(" ")) return MessageKey;

                var translated = Application.Current.TryFindResource(MessageKey) as string;
                return translated ?? MessageKey;
            }
        }

        public Brush ColorBrush => Type switch
        {
            LogType.Error => Brushes.Red,
            LogType.Warning => Brushes.Orange,
            LogType.Debug => Brushes.Gray,
            _ => Brushes.White
        };
    }

    public class AlarmService : ObservableObject
    {
        private static AlarmService _instance;
        public static AlarmService Instance => _instance ??= new AlarmService();

        // 1. 歷史紀錄 (永久保留，除非手動清空)
        public ObservableCollection<AlarmLog> AllLogs { get; private set; } = new();

        // 2. [新增] 活躍警報 (供 Header 輪播用，Clear 後清空)
        public ObservableCollection<AlarmLog> ActiveAlarms { get; private set; } = new();

        private const int MAX_API_LOGS = 50;       // API 紀錄保留筆數
        private const int MAX_HISTORY_LOGS = 500;  // 歷史紀錄總筆數上限

        /// <summary>
        /// [保留] 相容舊程式碼的字串介面
        /// </summary>
        public void AddLog(string typeStr, string msg)
        {
            LogType type = typeStr.ToUpper() switch
            {
                "ERR" or "ERROR" or "ALARM" => LogType.Error,
                "WARN" or "WARNING" => LogType.Warning,
                "DEBUG" or "API" => LogType.Debug,
                _ => LogType.Info
            };
            AddLog(type, msg);
        }

        /// <summary>
        /// [新增] 核心新增方法
        /// </summary>
        public void AddLog(LogType type, string messageKey)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 1. [過濾] API 輪詢過濾：若是 Debug 且包含 status/heartbeat 則忽略
                if (type == LogType.Debug &&
                   (messageKey.Contains("/v2/status") || messageKey.Contains("heartbeat")))
                {
                    return;
                }

                // 2. [過濾] 重複訊息過濾：若最新一筆跟現在這筆完全一樣，只更新時間
                var lastLog = AllLogs.FirstOrDefault();
                if (lastLog != null && lastLog.Type == type && lastLog.MessageKey == messageKey)
                {
                    lastLog.Time = DateTime.Now;
                    // 若需要讓 UI 更新時間顯示，可能需要重新賦值或實作 INPC
                    return;
                }

                // 3. 執行新增 (處理數量限制)
                // 3.1 針對 API/Debug 類型的個別限制
                if (type == LogType.Debug)
                {
                    var debugCount = AllLogs.Count(x => x.Type == LogType.Debug);
                    if (debugCount >= MAX_API_LOGS)
                    {
                        var oldest = AllLogs.LastOrDefault(x => x.Type == LogType.Debug);
                        if (oldest != null) AllLogs.Remove(oldest);
                    }
                }

                // 3.2 總歷史紀錄限制
                if (AllLogs.Count >= MAX_HISTORY_LOGS)
                {
                    AllLogs.RemoveAt(AllLogs.Count - 1);
                }

                var newLog = new AlarmLog
                {
                    Time = DateTime.Now,
                    Type = type,
                    MessageKey = messageKey
                };

                AllLogs.Insert(0, newLog);

                // 4. [新增] 處理活躍警報 (Warning & Error)
                if (type == LogType.Warning || type == LogType.Error)
                {
                    // 活躍清單也要防重複，避免跑馬燈出現兩次一樣的 Error
                    if (!ActiveAlarms.Any(x => x.MessageKey == messageKey))
                    {
                        ActiveAlarms.Add(newLog);
                    }
                }
            });
        }

        /// <summary>
        /// [新增] 清除活躍警報
        /// </summary>
        public void ClearActiveAlarms()
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 檢查是否包含 Error (需要記錄解除時間)
                bool hasError = ActiveAlarms.Any(x => x.Type == LogType.Error);

                ActiveAlarms.Clear();

                // 若剛剛清除了 Error，記錄一筆解除資訊
                if (hasError)
                {
                    // 請確保 Lang 檔中有定義 "Msg_ErrorReset" 或直接傳字串
                    AddLog(LogType.Info, "Errors Cleared (User Reset)");
                }
            });
        }
    }
}