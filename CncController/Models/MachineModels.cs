using System.Collections.Generic;

namespace CncController.Models
{
    // 1. 掃描到的 EtherCAT 裝置
    public class DiscoveredSlave
    {
        public int Index { get; set; }
        public string VendorId { get; set; }
        public string ProductCode { get; set; }
        public string Source { get; set; }
        public string Name { get; set; }

        // 顯示名稱
        public string DisplayName => $"#{Index}: {Name}";
    }
    // 2. 機台設定總表 (存檔用)  12我有變更阿
    public class MachineConfig
    {
        public int MasterIndex { get; set; } = 0;

        // 既有的軸設定
        public List<AxisSetting> Axes { get; set; } = new();

        // 既有的 IO 設定
        public List<IoSetting> IoMappings { get; set; } = new();

        // ★★★ [補上這段] 缺少的硬體對應設定 ★★★
        public List<HardwareMapping> Mappings { get; set; } = new List<HardwareMapping>();
    }

    // ★★★ [補上這個類別] 讓 SettingsViewModel 可以使用 HardwareMapping ★★★
    public class HardwareMapping
    {
        public string LogicalName { get; set; }      // 例如 "X Axis"
        public string PhysicalAddress { get; set; }  // 例如 "Slave_1_Panasonic" 
        // 您可以視需求增加更多欄位，例如 VendorID 等
    }

    // 3. 軸參數 (保持原樣)
    public class AxisSetting
    {
        public string AxisID { get; set; } = "X";
        public string Name { get; set; } = "X Axis";
        public double Pitch { get; set; } = 10.0;
        public double PulsePerRev { get; set; } = 10000;
        public double SoftLimitPos { get; set; } = 100.0;
        public double SoftLimitNeg { get; set; } = -100.0;
        public double HomeSpeed { get; set; } = 20.0;
    }

    // 4. IO 設定 (保持原樣)
    public class IoSetting
    {
        public int StationIndex { get; set; }
        public int PinIndex { get; set; }
        public string Function { get; set; } = string.Empty;
        public bool Invert { get; set; }
    }
}