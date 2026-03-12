using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace CncController.Models
{
    // [2026-03-12] 從 MachineModels.cs 拆分：IO 映射相關

    public partial class IoMapItem : ObservableObject
    {
        public int Index { get; set; }

        [ObservableProperty]
        private string _logicalName = string.Empty;

        [ObservableProperty]
        private DiscoveredSlave _selectedSlave;

        [ObservableProperty]
        private bool _isEnabled = true;

        public string DisplayName => SelectedSlave != null
            ? $"#{SelectedSlave.Index}: {SelectedSlave.Name} ({SelectedSlave.Category})"
            : LogicalName;

        partial void OnSelectedSlaveChanged(DiscoveredSlave value)
        {
            if (value == null)
            {
                PinSettings.Clear();
                OnPropertyChanged(nameof(DisplayName));
                return;
            }

            string pCode = value.ProductCode ?? "";
            int targetCount = (pCode.Contains("902") || value.Name.Contains("32")) ? 32 : 8;

            InitializePins(targetCount);
            OnPropertyChanged(nameof(DisplayName));
        }

        public ObservableCollection<IoPinSetting> PinSettings { get; } = new();

        public void InitializePins(int count)
        {
            if (PinSettings.Count == count)
            {
                OnPropertyChanged(nameof(PinSettings));
                return;
            }

            PinSettings.Clear();
            for (int i = 0; i < count; i++)
            {
                PinSettings.Add(new IoPinSetting
                {
                    PinIndex = i,
                    FunctionName = $"Pin {i}",
                    IsInverted = false
                });
            }
            OnPropertyChanged(nameof(PinSettings));
        }
    }

    public partial class IoPinSetting : ObservableObject
    {
        public int PinIndex { get; set; }

        [ObservableProperty]
        private string _functionName = string.Empty;

        [ObservableProperty]
        private bool _isInverted;

        [ObservableProperty]
        private bool _isActive;

        public string HalSignalName => $"din-{PinIndex:00}";
    }

    public static class StandardSignals
    {
        public static List<string> OutputSignals { get; } = new List<string>
        {
            "--- Custom / None ---",
            "coolant-flood",
            "coolant-mist",
            "spindle-on",
            "spindle-cw",
            "spindle-ccw",
            "spindle-brake",
            "machine-is-enabled",
            "estop-out",
            "digital-out-00",
            "digital-out-01",
            "digital-out-02",
            "digital-out-03"
        };

        public static List<string> InputSignals { get; } = new List<string>
        {
            "--- Custom / None ---",
            "estop-ext",
            "home-all",
            "probe-in",
            "cycle-start",
            "feed-hold",
            "spindle-inhibit"
        };
    }
}
