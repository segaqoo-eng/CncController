using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CncController.Models
{
    // ==========================================
    // 狀態資料結構 (對應 server.py /v2/status)
    // ==========================================
    public class MachineStatusData
    {
        public bool Connected { get; set; }
        public string Task_State { get; set; }      // ESTOP, ON, OFF
        public string Interp_State { get; set; }    // IDLE, RUNNING, PAUSED

        // [新增] 關鍵安全欄位
        public bool Is_Moving { get; set; }         // 機台是否正在移動中
        public bool Has_Error { get; set; }         // 是否有錯誤

        public Dictionary<string, double> Position { get; set; }
        public double Feedrate { get; set; }
        public double Spindle_Speed { get; set; }
        public string File { get; set; }

        // [新增] 警報列表
        public List<string> Alerts { get; set; }
    }

    // ==========================================
    // EtherCAT 掃描結果 (保持原樣)
    // ==========================================
    public class DiscoveredSlave
    {
        [JsonPropertyName("Slave")] public int Index { get; set; }
        [JsonPropertyName("VendorId")] public string VendorId { get; set; }
        [JsonPropertyName("ProductCode")] public string ProductCode { get; set; }
        [JsonPropertyName("Name")] public string Name { get; set; }
        [JsonPropertyName("Group")] public string VendorGroup { get; set; }
        [JsonPropertyName("Model")] public string ProductModel { get; set; }
        [JsonPropertyName("Category")] public string Category { get; set; }
        [JsonPropertyName("Source")] public string Source { get; set; }
        [JsonPropertyName("Mapped PDOs")] public string Pdos { get; set; }
        [JsonIgnore] public string DisplayName => $"#{Index}: {Name} ({VendorId})";
    }

    // ==========================================
    // 設定檔結構 (保持原樣)
    // ==========================================
    public class MachineConfig
    {
        public int MasterIndex { get; set; } = 0;
        public List<AxisSetting> Axes { get; set; } = new();
        public List<IoSetting> IoMappings { get; set; } = new();
        public List<HardwareMapping> Mappings { get; set; } = new();
    }

    public class HardwareMapping
    {
        public string LogicalName { get; set; }
        public string PhysicalAddress { get; set; }
        public int PhysicalIndex { get; set; }
        public string ExpectedVendorId { get; set; }
        public string ExpectedProductCode { get; set; }
    }

    public class AxisSetting
    {
        public int Index { get; set; }
        public string AxisID { get; set; } = "X";
        public string Name { get; set; } = "X Axis";
        public double Pitch { get; set; } = 10.0;
        public double PulsePerRev { get; set; } = 10000;
        public double SoftLimitPos { get; set; } = 100.0;
        public double SoftLimitNeg { get; set; } = -100.0;
        public double HomeSpeed { get; set; } = 20.0;
        public int HomeDirection { get; set; } = 1;
    }

    public class IoSetting
    {
        public int StationIndex { get; set; }
        public int PinIndex { get; set; }
        public string Function { get; set; } = string.Empty;
        public bool Invert { get; set; }
    }

    public enum LogType
    {
        Info,       // 一般訊息 (白色)
        Warning,    // 警告 (橘色) - 觸發跑馬燈
        Error,      // 錯誤 (紅色) - 觸發跑馬燈
        Debug       // 除錯/API (灰色) - 僅在 Debug 分頁顯示
    }
}