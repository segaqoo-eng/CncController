using CommunityToolkit.Mvvm.ComponentModel;
using System.Text.Json.Serialization;

namespace CncController.Models
{
    // [2026-03-12] 從 MachineModels.cs 拆分：刀具相關

    public partial class ToolEntry : ObservableObject
    {
        [ObservableProperty] private int _toolNumber;
        [ObservableProperty] private int _pocket;
        [ObservableProperty] private double _xOffset;
        [ObservableProperty] private double _yOffset;
        [ObservableProperty] private double _zOffset;
        [ObservableProperty] private double _aOffset;
        [ObservableProperty] private double _bOffset;
        [ObservableProperty] private double _cOffset;
        [ObservableProperty] private double _uOffset;
        [ObservableProperty] private double _vOffset;
        [ObservableProperty] private double _wOffset;
        [ObservableProperty] private double _diameter;
        [ObservableProperty] private double _frontAngle;
        [ObservableProperty] private double _backAngle;
        [ObservableProperty] private int _orientation;
        [ObservableProperty] private string _remark = "";
    }

    public class ToolLifeRaw
    {
        [JsonPropertyName("cutting_time_sec")]
        public double CuttingTimeSec { get; set; }
        [JsonPropertyName("change_count")]
        public int ChangeCount { get; set; }
        [JsonPropertyName("max_time_sec")]
        public int MaxTimeSec { get; set; }
        [JsonPropertyName("max_count")]
        public int MaxCount { get; set; }
    }

    public partial class ToolLifeEntry : ObservableObject
    {
        [ObservableProperty] private int _toolNumber;
        [ObservableProperty] private double _cuttingTimeSeconds;
        [ObservableProperty] private int _changeCount;
        [ObservableProperty] private int _maxTimeSeconds;
        [ObservableProperty] private int _maxChangeCount;

        public string CuttingTimeDisplay
        {
            get
            {
                var ts = System.TimeSpan.FromSeconds(CuttingTimeSeconds);
                return ts.TotalHours >= 1 ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m" : $"{ts.Minutes}m {ts.Seconds:D2}s";
            }
        }

        public double LifePercentTime => MaxTimeSeconds > 0
            ? System.Math.Min(CuttingTimeSeconds / MaxTimeSeconds * 100, 100) : 0;
        public double LifePercentCount => MaxChangeCount > 0
            ? System.Math.Min((double)ChangeCount / MaxChangeCount * 100, 100) : 0;
        public double MaxLifePercent => System.Math.Max(LifePercentTime, LifePercentCount);
        public bool IsOverLife => MaxLifePercent >= 100;
        public bool IsNearLife => MaxLifePercent >= 80 && MaxLifePercent < 100;
    }
}
