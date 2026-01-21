using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace CncController.Services
{
    public class AlarmItem
    {
        public DateTime Time { get; set; }
        public string Type { get; set; } // "ALARM", "WARNING", "INFO"
        public string Message { get; set; }
    }

    public class AlarmService
    {
        public static AlarmService Instance { get; } = new AlarmService();

        // 用於 UI 綁定的「即時警報清單」
        public ObservableCollection<AlarmItem> ActiveAlarms { get; } = new();

        private readonly string _logFilePath = "MachineHistory.log";

        private AlarmService() { }

        // 新增一筆警報/履歷
        public void AddLog(string type, string message, bool isActiveAlarm = false)
        {
            var item = new AlarmItem
            {
                Time = DateTime.Now,
                Type = type,
                Message = message
            };

            // 1. 如果是即時警報，加入 UI 清單
            if (isActiveAlarm)
            {
                // 避免重複加入相同的警報
                // (實務上可能需要更複雜的判斷，這裡先簡單處理)
                ActiveAlarms.Insert(0, item);
            }

            // 2. 寫入硬碟檔案 (Append)
            WriteToFile(item);
        }

        // 清除所有即時警報 (例如按下 ESC)
        public void AcknowledgeAll()
        {
            if (ActiveAlarms.Count > 0)
            {
                AddLog("OP", "User acknowledged all alarms.");
                ActiveAlarms.Clear();
            }
        }

        private async void WriteToFile(AlarmItem item)
        {
            try
            {
                string line = $"{item.Time:yyyy-MM-dd HH:mm:ss} | [{item.Type}] | {item.Message}";
                await File.AppendAllTextAsync(_logFilePath, line + Environment.NewLine);
            }
            catch
            {
                // 寫檔失敗暫不處理，避免影響主程式
            }
        }
    }
}