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

        // [2026-02-24] 新增 Work_Position：工件座標（= actual_position - g5x - g92 - tool）
        public Dictionary<string, double> Work_Position { get; set; }

        // [2026-02-24] 新增 Feed_Override / Spindle_Override：進給率/主軸覆蓋百分比
        public double Feed_Override { get; set; } = 100.0;
        public double Spindle_Override { get; set; } = 100.0;

        // [2026-02-24] 新增刀具資訊：刀具號、刀長（Z 軸補正）、刀徑
        public int Tool_Number { get; set; }
        public double Tool_Length { get; set; }
        public double Tool_Diameter { get; set; }

        // [2026-02-24] 新增 Homed：各軸原點復歸狀態
        public Dictionary<string, bool> Homed { get; set; }

        // [2026-02-24] 新增 G92 偏移量（供 Offsets 右欄 G52/G92 OFFSET 欄位）
        public Dictionary<string, double> G92_Offset { get; set; }

        // [2026-02-24] 新增完整刀具偏移（供 Offsets 右欄 TOOL OFFSET 欄位）
        public Dictionary<string, double> Tool_Offset_XYZ { get; set; }

        // [2026-02-24] 新增任務模式（MANUAL/AUTO/MDI，供 Offsets 右下角模式按鈕高亮）
        public string Task_Mode { get; set; }

        // [2026-03-04] 新增 Block Delete / Optional Stop / Current Line
        public bool Block_Delete { get; set; }
        public bool Optional_Stop { get; set; }
        public int Current_Line { get; set; }
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

    // [2026-02-24] 新增 MachineType：CNC 機台類型定義（3/4/5/6 軸可配置）
    //   三軸 VMC：XYZ（最基礎立式加工中心）
    //   四軸：XYZ + A（第四軸分度盤）
    //   五軸搖籃式：XYZ + AC（工件旋轉，主軸固定）
    //   五軸擺頭式：XYZ + BC（主軸旋轉，工件固定）
    //   六軸：XYZABC（完整六自由度）
    public enum MachineType
    {
        ThreeAxis,          // XYZ
        FourAxisA,          // XYZ + A（繞 X 軸旋轉）
        FourAxisB,          // XYZ + B（繞 Y 軸旋轉）
        FiveAxisTrunnion,   // XYZ + AC（搖籃式）
        FiveAxisSwivel,     // XYZ + BC（主軸擺頭式）
        SixAxis             // XYZABC
    }

    public class MachineConfig
    {
        public int MasterIndex { get; set; } = 0;
        // [2026-02-24] 新增 MachineType：預設三軸 VMC
        public MachineType MachineType { get; set; } = MachineType.ThreeAxis;
        public List<AxisSetting> Axes { get; set; } = new();
        public List<IoSetting> IoMappings { get; set; } = new();
        public List<HardwareMapping> Mappings { get; set; } = new();

        // [2026-02-24] 依 MachineType 取得啟用軸列表
        public List<string> GetEnabledAxes()
        {
            return MachineType switch
            {
                MachineType.FourAxisA => new() { "X", "Y", "Z", "A" },
                MachineType.FourAxisB => new() { "X", "Y", "Z", "B" },
                MachineType.FiveAxisTrunnion => new() { "X", "Y", "Z", "A", "C" },
                MachineType.FiveAxisSwivel => new() { "X", "Y", "Z", "B", "C" },
                MachineType.SixAxis => new() { "X", "Y", "Z", "A", "B", "C" },
                _ => new() { "X", "Y", "Z" } // ThreeAxis
            };
        }
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

    // [2026-03-03] 新增 ToolEntry：刀具表條目（對齊 PB 版 TOOL 分頁 DataGrid）
    public partial class ToolEntry : ObservableObject
    {
        [ObservableProperty] private int _toolNumber;
        [ObservableProperty] private int _pocket;
        [ObservableProperty] private double _xOffset;
        [ObservableProperty] private double _yOffset;
        [ObservableProperty] private double _zOffset;
        [ObservableProperty] private double _aOffset;
        [ObservableProperty] private double _bOffset;
        [ObservableProperty] private double _cOffset;
        [ObservableProperty] private double _uOffset;
        [ObservableProperty] private double _vOffset;
        [ObservableProperty] private double _wOffset;
        [ObservableProperty] private double _diameter;
        // [2026-03-03] 新增 FNT ANG / BAK ANG / ORIENT（對齊 PB 版 TOOLPARAM）
        [ObservableProperty] private double _frontAngle;
        [ObservableProperty] private double _backAngle;
        [ObservableProperty] private int _orientation;
        // [2026-03-03] 欄位名稱對齊後端 Remark
        [ObservableProperty] private string _remark = "";
    }

    // [2026-03-04] 探測結果（後端 /v2/probe/run 回傳）
    public class ProbeResult
    {
        public bool Tripped { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
        public string Error { get; set; }
        // [2026-03-04] Edge Angle / Calibrate 擴展欄位
        public double Angle { get; set; }
        public double EdgeWidth { get; set; }
        // [2026-03-04] Boss/Pocket 實測 X/Y 寬度
        public double WidthX { get; set; }
        public double WidthY { get; set; }
    }

    // [2026-03-04] 探測參數（前端 ProbingViewModel → 後端 /v2/probe/run）
    public class ProbeParameters
    {
        public double TraverseSpeed { get; set; } = 300.0;
        public double SearchSpeed { get; set; } = 50.0;
        public double MaxXYDistance { get; set; } = 20.0;
        public double MaxZDistance { get; set; } = 20.0;
        public double XYClearance { get; set; } = 5.0;
        public double ZClearance { get; set; } = 5.0;
        public double ExtraDepth { get; set; } = 2.0;
        // [2026-03-04] Boss/Pocket 近似直徑
        public double Diameter { get; set; } = 20.0;
        // [2026-03-04] Boss/Pocket 特徵中心相對當前位置的近似偏移
        public double OffsetX { get; set; }
        public double OffsetY { get; set; }
        // [2026-03-04] Edge Angle 邊緣寬度（使用者輸入，作為探測間距）
        public double EdgeWidth { get; set; }
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