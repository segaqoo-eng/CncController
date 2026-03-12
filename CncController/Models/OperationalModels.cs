using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CncController.Models
{
    // [2026-03-12] 從 MachineModels.cs 拆分：加工統計/維護/斷電續切/備份

    public class MachiningStats
    {
        [JsonPropertyName("total_seconds")]
        public double TotalSeconds { get; set; }
        [JsonPropertyName("cycle_count")]
        public int CycleCount { get; set; }
        [JsonPropertyName("last_file")]
        public string LastFile { get; set; } = "";

        public string TotalTimeDisplay
        {
            get
            {
                var ts = System.TimeSpan.FromSeconds(TotalSeconds);
                return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m" : $"{ts.Minutes}m {ts.Seconds:D2}s";
            }
        }
    }

    public partial class MaintenanceItem : ObservableObject
    {
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private double _intervalHours;
        [ObservableProperty] private double _accumulatedHours;
        [ObservableProperty] private double _lastResetTime;

        public double RemainingHours => System.Math.Max(0, IntervalHours - AccumulatedHours);
        public double ProgressPercent => IntervalHours > 0
            ? System.Math.Min(AccumulatedHours / IntervalHours * 100, 100) : 0;
        public bool IsDue => IntervalHours > 0 && AccumulatedHours >= IntervalHours;
        public bool IsNearDue => IntervalHours > 0 && ProgressPercent >= 80 && !IsDue;

        public string AccumulatedDisplay
        {
            get
            {
                if (AccumulatedHours < 1) return $"{AccumulatedHours * 60:F0} min";
                return $"{AccumulatedHours:F1} hr";
            }
        }
    }

    public class ResumeState
    {
        [JsonPropertyName("file")]
        public string File { get; set; } = "";
        [JsonPropertyName("line")]
        public int Line { get; set; }
        [JsonPropertyName("tool")]
        public int Tool { get; set; }
        [JsonPropertyName("wcs")]
        public int Wcs { get; set; }
        [JsonPropertyName("timestamp")]
        public double Timestamp { get; set; }

        public string TimestampDisplay => System.DateTimeOffset.FromUnixTimeSeconds((long)Timestamp).LocalDateTime.ToString("yyyy/MM/dd HH:mm:ss");
    }

    public class BackupInfo
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [JsonPropertyName("file_count")]
        public int FileCount { get; set; }
        [JsonPropertyName("size_bytes")]
        public long SizeBytes { get; set; }
        public string DisplaySize => SizeBytes < 1024 ? $"{SizeBytes} B"
            : SizeBytes < 1048576 ? $"{SizeBytes / 1024.0:F1} KB"
            : $"{SizeBytes / 1048576.0:F1} MB";
    }

    public class BackupCreateResult
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        [JsonPropertyName("file_count")]
        public int FileCount { get; set; }
        [JsonPropertyName("size_bytes")]
        public long SizeBytes { get; set; }
    }

    public class BackupRestoreResult
    {
        [JsonPropertyName("restored")]
        public List<string> Restored { get; set; } = new();
        [JsonPropertyName("frontend_configs")]
        public Dictionary<string, string> FrontendConfigs { get; set; } = new();
    }

    // [2026-03-12] 從 HistoryViewModel.cs 搬入：報警統計項目（Phase 2）
    public partial class AlarmStatItem : ObservableObject
    {
        [ObservableProperty] private string _message = "";
        [ObservableProperty] private string _type = "";
        [ObservableProperty] private int _count;
        [ObservableProperty] private string _lastTime = "";
    }
}
