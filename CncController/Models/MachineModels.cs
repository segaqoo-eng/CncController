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
        public string DisplayName => $"#{Index}: {Name} ({VendorId})";
    }

    // 2. 機台設定總表
    public class MachineConfig
    {
        public int MasterIndex { get; set; } = 0;
        public List<AxisSetting> Axes { get; set; } = new();
        public List<IoSetting> IoMappings { get; set; } = new();

        // [更新] 硬體對應清單
        public List<HardwareMapping> Mappings { get; set; } = new List<HardwareMapping>();
    }

    // 3. [新增] 硬體對應類別 (包含嚴格檢查欄位)
    public class HardwareMapping
    {
        public string LogicalName { get; set; }      // 例如 "X Axis"
        public string PhysicalAddress { get; set; }  // 顯示名稱

        // [關鍵] 嚴格比對欄位
        public int PhysicalIndex { get; set; }       // 站號 (Index)
        public string ExpectedVendorId { get; set; } // 預期的廠商ID (例如 "0x000001dd")
        public string ExpectedProductCode { get; set; } // 預期的產品碼
    }

    // 4. 軸參數 (保持不變)
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

    // 5. IO 設定 (保持不變)
    public class IoSetting
    {
        public int StationIndex { get; set; }
        public int PinIndex { get; set; }
        public string Function { get; set; } = string.Empty;
        public bool Invert { get; set; }
    }
}