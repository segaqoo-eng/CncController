using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CncController.Models
{
   
    // ==========================================
    // 2. 狀態資料結構 (保持原樣)
    // ==========================================
    public class MachineStatusData
    {
        public bool Connected { get; set; }
        public string Task_State { get; set; }
        public string Interp_State { get; set; }
        public bool Is_Moving { get; set; }
        public bool Has_Error { get; set; }
        public Dictionary<string, double> Position { get; set; }
        public double Feedrate { get; set; }
        public double Spindle_Speed { get; set; }
        public string File { get; set; }
        public List<string> Alerts { get; set; }

        // ★★★ [新增] 伺服底層 IO 資料 ★★★
        // 對應 JSON: "Servo_IO": { "0": {"DI": "...", "Status": "..."}, "1": ... }
        public Dictionary<string, ServoIoRawData> Servo_IO { get; set; }
    }

    // ==========================================
    // 3. EtherCAT 掃描結果 (保持原樣)
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
    // 4. 設定檔結構
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

    // ==========================================
    // ★★★ [修正版] 軸參數設定 (使用 Enums) ★★★
    // ==========================================
    public class AxisSetting
    {
        // --- 基本參數 ---
        public int Index { get; set; }
        public string AxisID { get; set; } = "X";
        public string Name { get; set; } = "X Axis";

        // --- 運動參數 ---
        public double Pitch { get; set; } = 5.0;
        public double PulsePerRev { get; set; } = 10000;
        public double MaxVelocity { get; set; } = 100.0;
        public double MaxAcceleration { get; set; } = 500.0;
        public bool InvertMotor { get; set; } = false;

        // --- 軟體極限 ---
        public double SoftLimitPos { get; set; } = 100.0;
        public double SoftLimitNeg { get; set; } = -100.0;

        // --- 原點復歸參數 ---
        // ★★★ 修正：使用 Enum 取代舊邏輯 ★★★
        public HomingMode HomingMode { get; set; } = HomingMode.HomeSwitch; // 回原點模式

        public double HomeSpeed { get; set; } = 10.0;       // Search Vel
        public double HomeLatchSpeed { get; set; } = 1.0;   // Latch Vel
        public int HomeDirection { get; set; } = 1;         // 方向
        public double HomeOffset { get; set; } = 0.0;       // Offset
        public int HomeSequence { get; set; } = 0;          // 順序

        // 是否找 Z 相 (這還是需要獨立的 Bool，因為是選項)
        public bool HomeUseIndex { get; set; } = true;

        // --- 硬體 IO 設定 (DI Mapping) ---
        // ★★★ 新增：DI 對應索引 (0~15) ★★★
        public int HomeDiIndex { get; set; } = 0;     // 原點開關 DI
        public int PosLimitDiIndex { get; set; } = 1; // 正極限 DI
        public int NegLimitDiIndex { get; set; } = 2; // 負極限 DI

        // --- 極限開關邏輯 ---
        // ★★★ 修正：使用 Enum 取代 bool IsLimitSensorsNC ★★★
        public LimitLogic LimitSwitchLogic { get; set; } = LimitLogic.NC; // 預設常閉
         // [2026-02-05 新增] 原點開關的邏輯 (獨立控制)
        public LimitLogic HomeSwitchLogic { get; set; } = LimitLogic.NO;
    }

    public class IoSetting
    {
        public int StationIndex { get; set; }
        public int PinIndex { get; set; }
        public string Function { get; set; } = string.Empty;
        public bool Invert { get; set; }
    }
    

    // ★★★ [新增] 用來接收 Raw Data 的小類別 ★★★
    public class ServoIoRawData
    {
        public string DI { get; set; }     // e.g., "0x00000000"
        public string Status { get; set; } // e.g., "0x00604137"
    }
}