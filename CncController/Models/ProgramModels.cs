using CommunityToolkit.Mvvm.ComponentModel;

namespace CncController.Models
{
    // [2026-03-12] 從 MachineModels.cs 拆分：程式檔案 & 巨集變數

    public class ProgramFileInfo
    {
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public double Modified { get; set; }
    }

    public class ProgramReadResult
    {
        public string Name { get; set; } = "";
        public string Content { get; set; } = "";
    }

    public partial class MacroVariable : ObservableObject
    {
        [ObservableProperty] private int _id;
        [ObservableProperty] private double _value;
        [ObservableProperty] private string _name = "";
        [ObservableProperty] private bool _isModified;
    }
}
