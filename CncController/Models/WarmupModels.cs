using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace CncController.Models
{
    // [2026-03-12] 從 MachineModels.cs 拆分：主軸暖機相關

    public partial class WarmupStep : ObservableObject
    {
        [ObservableProperty] private int _rpm;
        [ObservableProperty] private int _durationSeconds;
        [ObservableProperty] private string _stepStatus = "";
    }

    public class SpindleWarmupConfig
    {
        public List<WarmupStepData> Steps { get; set; } = new()
        {
            new() { Rpm = 500,  DurationSeconds = 60 },
            new() { Rpm = 1000, DurationSeconds = 60 },
            new() { Rpm = 2000, DurationSeconds = 60 },
            new() { Rpm = 4000, DurationSeconds = 60 },
            new() { Rpm = 8000, DurationSeconds = 60 },
        };
    }

    public class WarmupStepData
    {
        public int Rpm { get; set; }
        public int DurationSeconds { get; set; }
    }
}
