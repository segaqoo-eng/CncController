using System.Collections.Generic;

namespace CncController.Models
{
    // 1. 掃描到的 EtherCAT 裝置
    public class DiscoveredSlave
    {
        public int Index { get; set; }
        public string VendorId { get; set; }
        public string ProductCode { get; set; }
        public string Source { get; set; } // 這是您程式碼裡有的
        public string Name { get; set; }

        // 新增這個屬性：為了在下拉選單顯示 "Slave 1: Delta Drive" 這種好讀的格式
        public string DisplayName => $"#{Index}: {Name}";
    }

    // 2. 機台設定總表 (存檔用)
    public class MachineConfig
    {
        public int MasterIndex { get; set; } = 0;
        public List<AxisSetting> Axes { get; set; } = new();
        public List<IoSetting> IoMappings { get; set; } = new();
    }

    // 3. 軸參數
    public class AxisSetting
    {
        public string AxisID { get; set; } = "X"; // X, Y, Z
        public string Name { get; set; } = "X Axis";
        public double Pitch { get; set; } = 10.0;       // 導程
        public double PulsePerRev { get; set; } = 10000; // 解析度
        public double SoftLimitPos { get; set; } = 100.0;
        public double SoftLimitNeg { get; set; } = -100.0;
        public double HomeSpeed { get; set; } = 20.0;
    }

    // 4. IO 設定
    public class IoSetting
    {
        public int StationIndex { get; set; }
        public int PinIndex { get; set; }
        public string Function { get; set; } = string.Empty;
        public bool Invert { get; set; }
    }
}