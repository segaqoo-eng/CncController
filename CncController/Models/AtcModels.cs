using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;

namespace CncController.Models
{
    // [2026-03-12] 從 MachineModels.cs 拆分：ATC 刀庫相關

    public class AtcStatus
    {
        public int Pockets { get; set; }
        public int CurrentPocket { get; set; }
        public double CarouselAngle { get; set; }
        public int ToolInSpindle { get; set; }
        public string ControlMode { get; set; } = "SERVO";
        public Dictionary<string, bool> DO { get; set; } = new();
        public Dictionary<string, bool> DI { get; set; } = new();
        public Dictionary<string, int> SlotTools { get; set; } = new();
    }

    public partial class AtcSlotInfo : ObservableObject
    {
        [ObservableProperty] private int _slotNumber;
        [ObservableProperty] private int _toolNumber;
        public bool HasTool => ToolNumber > 0;

        partial void OnToolNumberChanged(int value)
        {
            OnPropertyChanged(nameof(HasTool));
        }
    }
}
