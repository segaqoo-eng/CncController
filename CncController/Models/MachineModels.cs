using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json.Serialization;
using System.Diagnostics;
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
        public Dictionary<string, double> DTG { get; set; }
        public double Feedrate { get; set; }
        public double Spindle_Speed { get; set; }
        public string File { get; set; }
        public List<string> Alerts { get; set; }

        // ★★★ [新增] 伺服底層 IO 資料 ★★★
        // 對應 JSON: "Servo_IO": { "0": {"DI": "...", "Status": "..."}, "1": ... }
        public Dictionary<string, ServoIoRawData> Servo_IO { get; set; }

        // [2026-02-23] 新增 Active_WCS：對應後端 /v2/status 回傳的目前工件座標系（G54–G59）
        public string Active_WCS { get; set; } = "G54";
    }

    // ==========================================
    // 3. EtherCAT 掃描結果 (保持原樣)
    // ==========================================

    // 對應後端 Python 的 DeviceCategory
    // 這不是 Enum，這只是裝字串的容器
    public static class DeviceCategory
    {
        public const string Servo = "Servo";
        public const string DiDo = "DI+DO";   // 對應 Python
        public const string DigIn = "DigIn";
        public const string DigOut = "DigOut";
        public const string Mpg = "MPG";
        public const string DA = "DA";
        public const string AD = "AD";
        public const string Coupler = "Coupler";
        public const string Unknown = "Unknown";
    }

    // [新增] 定義映射用途
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum MapType
    {
        Axis,   // 用於軸映射
        Input,  // 用於輸入映射
        Output  // 用於輸出映射
    }

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
        // 類型標記： "Axis" (軸), "Input" (輸入), "Output" (輸出)
        
        public MapType Type { get; set; } = MapType.Axis;
        // 通道索引：紀錄這是第幾組 (例如 Input 0, Input 1)
        public int ChannelIndex { get; set; }
        // ★★★ [新增] 儲存 32 個 Pin 的設定 ★★★
        public List<PinConfig> Pins { get; set; } = new();
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
    // [新增] IO 映射項目類別
    public partial class IoMapItem : ObservableObject
    {
        public int Index { get; set; }

        [ObservableProperty]
        private string _logicalName = string.Empty;

        // [ObservableProperty] 會自動產生 SelectedSlave 屬性
        // 並自動呼叫 partial void OnSelectedSlaveChanged(DiscoveredSlave value)
        [ObservableProperty]
        private DiscoveredSlave _selectedSlave;

        // ★★★ [關鍵修正] 當下拉選單改變時的處理邏輯 ★★★
        partial void OnSelectedSlaveChanged(DiscoveredSlave value)
        {
            if (value == null || value.Name == "--- None ---")
            {
                PinSettings.Clear();
                return;
            }

            string pCode = value.ProductCode ?? "";
            int targetCount = (pCode.Contains("902") || value.Name.Contains("32")) ? 32 : 8;

            // 呼叫初始化邏輯
            InitializePins(targetCount);
        }

        public ObservableCollection<IoPinSetting> PinSettings { get; } = new();

        public void InitializePins(int count)
        {
            // 如果數量已經符合，我們不需要 Clear 再 Add (會洗掉載入的設定)
            // 但我們必須確保通知 UI 重新綁定
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

    // [新增] 單一 IO 接點的設定
    public partial class IoPinSetting : ObservableObject
    {
        // 接點編號 (0~31)
        public int PinIndex { get; set; }

        // 功能描述 (例如: Home X, Start Button)
        [ObservableProperty]
        private string _functionName = string.Empty;

        // 是否反轉 (False = NO 常開, True = NC 常閉)
        [ObservableProperty]
        private bool _isInverted;

        // 對應到的 HAL 訊號名稱 (自動生成用，例如: input-00)
        public string HalSignalName => $"din-{PinIndex:00}";
    }
    // [新增] 用於儲存單一 Pin 設定的輕量級類別 (存檔用)
    public class PinConfig
    {
        public int Index { get; set; }
        public string Function { get; set; }
        public bool IsInverted { get; set; } // True = NC, False = NO
    }

    public static class StandardSignals
    {
        // 定義常用的 Output 訊號名稱 (必須與 GenerateHal 中的名稱一致)
        public static List<string> OutputSignals { get; } = new List<string>
        {
            "--- Custom / None ---", // 空白選項
            "coolant-flood",         // M8 開水
            "coolant-mist",          // M7 噴霧/吸塵
            "spindle-on",            // 主軸運轉 (M3/M4)
            "spindle-cw",            // 主軸正轉 (M3)
            "spindle-ccw",           // 主軸反轉 (M4)
            "spindle-brake",         // 主軸煞車 (M5)
            "machine-is-enabled",    // 系統啟用狀態
            "estop-out",             // 觸發外部急停
            "digital-out-00",        // 通用輸出 (M64 P0)
            "digital-out-01",        // 通用輸出 (M64 P1)
            "digital-out-02",
            "digital-out-03"
        };

        // (選用) 如果輸入也要做下拉，可以定義這裡
        public static List<string> InputSignals { get; } = new List<string>
        {
            "--- Custom / None ---",
            "estop-ext",             // 外部急停按鈕
            "home-all",              // 全軸回原點觸發
            "probe-in",              // 探針訊號
            "cycle-start",           // 循環啟動按鈕
            "feed-hold",             // 進給暫停按鈕
            "spindle-inhibit"        // 禁止主軸啟動
        };
    }
}