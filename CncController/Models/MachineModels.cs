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

        // [2026-03-06] 新增 Probe_Input：探針輸入訊號即時狀態
        public bool Probe_Input { get; set; }
        // [2026-03-04] 新增 Block Delete / Optional Stop / Current Line
        public bool Block_Delete { get; set; }
        public bool Optional_Stop { get; set; }
        public int Current_Line { get; set; }

        // [2026-03-09] 新增主軸 Encoder 角度（0~360°，從 spindle.0.revs 換算）
        public double Spindle_Position { get; set; }

        // [2026-03-10] 新增主軸方向：0=停止, 1=CW正轉(M3), -1=CCW反轉(M4)
        public int Spindle_Direction { get; set; }

        // [2026-03-10] 新增 IO 即時狀態（slave index → { "di": {pin→bool}, "do": {pin→bool} }）
        // 供 IN MAP / OUT MAP 即時指示燈
        public Dictionary<string, Dictionary<string, Dictionary<string, bool>>> IO_Status { get; set; }
    }

    // ==========================================
    // 3. EtherCAT 掃描結果 (保持原樣)
    // ==========================================

    // 對應後端 Python 的 DeviceCategory
    // 這不是 Enum，這只是裝字串的容器
    public static class DeviceCategory
    {
        public const string Servo = "Servo";
        public const string PulseGen = "PulseGen";  // [2026-03-05] 新增：台達 5621 脈波產生器
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

    // [2026-03-05] 新增 SlaveDeviceType：統一設備類型判斷（取代散落的字串比對）
    public enum SlaveDeviceType
    {
        Servo,          // CiA 402 伺服驅動器（匯川/台達 6080 等）— 閉環：真實編碼器回授
        PulseGenerator, // 脈波產生器（台達 5621）— 有 CiA 402 但無 6060/6061 PDO，編碼器數值 = 命令座標（開環）
        IoModule,       // IO 模組（台達 R2-EC0902）— DI+DO 混合 IO，Free Run
        DigitalInput,   // [2026-03-05] 純數位輸入模組
        DigitalOutput,  // [2026-03-05] 純數位輸出模組
        Mpg,            // [2026-03-05] 手輪 (Manual Pulse Generator)
        Coupler,        // [2026-03-05] 匯流排耦合器（跳過 PDO）
        Unknown         // 未識別設備
    }

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

    // [2026-03-06] 新增 AtcType：刀庫類型定義
    public enum AtcType
    {
        None,       // 無刀庫（手動換刀）
        Turret,     // 排刀式（Rack，固定刀座，主軸移動取刀）
        Umbrella,   // 斗笠式（Carousel，旋轉刀盤 + 升降機構）
        SideMount   // 刀臂式（保留，暫不實作）
    }

    // [2026-03-09] 新增 CarouselControlMode：刀盤控制模式（Servo 伺服定位 / IO 馬達+感測器計數）
    public enum CarouselControlMode
    {
        Servo,  // 伺服定角度：EtherCAT 伺服直接旋轉到目標角度，不需 RotationIndex
        IO      // IO 感測器：普通馬達正反轉 + RotationIndex 感測器計數到位
    }

    // [2026-03-06] 主軸伺服設定（剛性攻牙 / M19 定向）
    public class SpindleConfig
    {
        public int SlaveIndex { get; set; } = -1;          // EtherCAT Slave 站號
        public int EncoderPPR { get; set; } = 4096;        // 編碼器脈衝/圈
        public double MaxRPM { get; set; } = 8000;         // 最大轉速
        public double MaxAccel { get; set; } = 2000;       // 最大加速度 RPM/s
        public double OrientAngle { get; set; } = 0.0;     // M19 定向角度 (deg)
        public bool RigidTappingEnabled { get; set; } = true; // 啟用剛性攻牙 G33.1
    }

    public class AtcConfig
    {
        public AtcType Type { get; set; } = AtcType.None;
        public int ToolCount { get; set; } = 12;

        // [2026-03-09] 刀盤控制模式（Servo=伺服定位 / IO=馬達+感測器）
        public CarouselControlMode ControlMode { get; set; } = CarouselControlMode.Servo;

        // EtherCAT Servo（斗笠旋轉伺服，進階選項）
        public int CarouselSlaveIndex { get; set; } = -1;
        public double CarouselMaxVel { get; set; } = 90.0;
        public double CarouselMaxAccel { get; set; } = 360.0;
        public int CarouselPulsePerRev { get; set; } = 10000;   // [2026-03-06] 編碼器脈衝/圈
        public double CarouselPitch { get; set; } = 360.0;     // [2026-03-06] 每圈行程（旋轉軸=360度）

        // [2026-03-09] IO DO（對應 M64/M65 P-word）— 預設從 31 倒數，避免與冷卻/主軸(0~5)衝突
        public int IoSlaveIndex { get; set; } = -1;
        public int DoCarouselOut { get; set; } = 31;
        public int DoCarouselHome { get; set; } = 30;
        public int DoDrawbar { get; set; } = 29;
        public int DoAirBlow { get; set; } = 28;
        public int DoMotorFwd { get; set; } = 27;
        public int DoMotorRev { get; set; } = 26;

        // [2026-03-09] IO DI（對應 M66 P-word）— 預設從 31 倒數，避免與通用 DI 衝突
        public int DiCarouselHome { get; set; } = 31;
        public int DiCarouselOut { get; set; } = 30;
        public int DiDrawbarClamp { get; set; } = 29;
        public int DiDrawbarUnclamp { get; set; } = 28;
        public int DiRotationIndex { get; set; } = 27;

        // 時序（ms）
        public int ClampDwell { get; set; } = 1000;
        public int UnclampDwell { get; set; } = 1000;
        public int AirBlowDwell { get; set; } = 300;
        public int SensorTimeout { get; set; } = 5000;

        // 安全位置（機台座標）
        public double ZToolChangeHeight { get; set; } = -3.9;
        public double ZClearanceHeight { get; set; } = 0.0;

        // Rack 參數
        public double RackTraverseSpeed { get; set; } = 3000;
        public double RackPocket1X { get; set; } = 0.0;
        public double RackPocket1Y { get; set; } = 0.0;
        public double RackPocket2X { get; set; } = 0.0;
        public double RackPocket2Y { get; set; } = 0.0;
        public double RackClearanceX { get; set; } = 0.0;
        public double RackClearanceY { get; set; } = 0.0;
    }

    // [2026-03-09] ATC 即時狀態（從後端 /v2/atc/status 回傳）
    public class AtcStatus
    {
        public int Pockets { get; set; }
        public int CurrentPocket { get; set; }
        public double CarouselAngle { get; set; } // [2026-03-09] Servo encoder 真實角度
        public int ToolInSpindle { get; set; }
        public string ControlMode { get; set; } = "SERVO"; // [2026-03-09] SERVO / IO
        public Dictionary<string, bool> DO { get; set; } = new();
        public Dictionary<string, bool> DI { get; set; } = new();
        public Dictionary<string, int> SlotTools { get; set; } = new();
    }

    // [2026-03-06] 新增 AtcSlotInfo：刀位狀態資訊（供 UI 刀盤視覺化綁定）
    public partial class AtcSlotInfo : ObservableObject
    {
        [ObservableProperty] private int _slotNumber;      // 刀位號 1~N
        [ObservableProperty] private int _toolNumber;      // 刀具號（0=空位）
        public bool HasTool => ToolNumber > 0;

        // [2026-03-06] 當 ToolNumber 改變時通知 HasTool
        partial void OnToolNumberChanged(int value)
        {
            OnPropertyChanged(nameof(HasTool));
        }
    }

    public class MachineConfig
    {
        public int MasterIndex { get; set; } = 0;
        // [2026-02-24] 新增 MachineType：預設三軸 VMC
        public MachineType MachineType { get; set; } = MachineType.ThreeAxis;
        public List<AxisSetting> Axes { get; set; } = new();
        public List<IoSetting> IoMappings { get; set; } = new();
        public List<HardwareMapping> Mappings { get; set; } = new();
        // [2026-03-06] 新增 ATC 刀庫設定
        public AtcConfig Atc { get; set; } = new();
        // [2026-03-06] 主軸伺服設定
        public SpindleConfig Spindle { get; set; } = new();

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
        // [2026-03-05] 儲存掃描結果的設備類別（優先用於 ClassifyDevice，避免重算）
        public string DeviceCategory { get; set; } = "";
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

        // [2026-03-05] 新增：啟用勾選（預設 true）
        [ObservableProperty]
        private bool _isEnabled = true;

        // [2026-03-05] 新增：顯示名稱（站號 + 設備名 + 類型）
        public string DisplayName => SelectedSlave != null
            ? $"#{SelectedSlave.Index}: {SelectedSlave.Name} ({SelectedSlave.Category})"
            : LogicalName;

        // [2026-03-05] 改為純列表模式，移除 "--- None ---" 判斷
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

            // 呼叫初始化邏輯
            InitializePins(targetCount);
            OnPropertyChanged(nameof(DisplayName));
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

        // [2026-03-10] 即時狀態（true=ON, false=OFF，供 IN MAP / OUT MAP 指示燈）
        [ObservableProperty]
        private bool _isActive;

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

    // [2026-03-11] 程式檔案資訊（後端 /v2/program/list 回傳）
    public class ProgramFileInfo
    {
        public string Name { get; set; } = "";
        public long Size { get; set; }
        public double Modified { get; set; }
    }

    // [2026-03-11] 程式檔案回讀結果
    public class ProgramReadResult
    {
        public string Name { get; set; } = "";
        public string Content { get; set; } = "";
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
        // [2026-03-06] 探針刀號（後端自動 G43 + 讀取直徑做半徑補正）
        public int ProbeToolNumber { get; set; } = 0;
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