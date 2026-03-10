// [2026-03-03] 新增 AtcViewModel：ATC 自動刀庫分頁 ViewModel
//              MANUAL ATC（手動按鈕）/ PROGRAM TOOLS（程式刀具列表）雙模式切換
//              所有指令透過 MachineControlService.SendMdiCommandAsync() 送出
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Threading;
using CncController.Models;
using CncController.Services;
using CncController.Helpers;

namespace CncController.ViewModels
{
    public partial class AtcViewModel : ObservableObject
    {
        // [2026-03-06] 編譯時刀庫類型（手動修改此變數切換刀庫類型）
        private const AtcType ACTIVE_ATC_TYPE = AtcType.Umbrella;

        // [2026-03-03] 即時機台狀態（刀具號/刀長/刀徑 — 從 MainViewModel 傳入）
        [ObservableProperty] private MachineStatus _machineStatus;

        // [2026-03-03] 模式切換：ManualAtc / ProgramTools
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsManualMode))]
        [NotifyPropertyChangedFor(nameof(IsProgramMode))]
        private string _selectedMode = "ManualAtc";

        public bool IsManualMode => SelectedMode == "ManualAtc";
        public bool IsProgramMode => SelectedMode == "ProgramTools";

        // [2026-03-03] 右面板刀具號輸入
        [ObservableProperty] private int _changeToolNumber = 1;

        // [2026-03-03] MDI 自由輸入
        [ObservableProperty] private string _mdiInput = "";

        // [2026-03-03] REMARK 顯示
        [ObservableProperty] private string _remarkText = "";

        // [2026-03-10] 刀位設定：選擇的刀位號（1~N）+ 要寫入的刀號
        [ObservableProperty] private int _selectedSlotNumber = 1;
        [ObservableProperty] private int _setSlotToolNumber = 1;

        // [2026-03-06] 刀盤屬性
        [ObservableProperty] private double _carouselAngle = 0;        // [2026-03-06] 刀盤旋轉角度
        [ObservableProperty] private int _currentPocket = 1;            // [2026-03-06] 當前刀位號
        [ObservableProperty] private bool _isReferenced = false;        // [2026-03-06] 是否已歸零
        [ObservableProperty] private bool _isAtcBusy = false;           // [2026-03-06] 換刀進行中
        [ObservableProperty] private string _atcStatusText = "未歸零"; // [2026-03-10] 狀態文字

        // [2026-03-10] DO/DI 即時狀態（根據後端回傳的 motion.digital-out/in 更新）
        [ObservableProperty] private bool _isCarouselOut;     // 刀盤伸出
        [ObservableProperty] private bool _isCarouselHome;    // 刀盤收回
        [ObservableProperty] private bool _isDrawbarOn;       // 拉桿（夾/鬆刀）
        [ObservableProperty] private bool _isAirBlowOn;       // 吹氣
        [ObservableProperty] private bool _isMotorFwd;        // 馬達正轉
        [ObservableProperty] private bool _isMotorRev;        // 馬達反轉
        // [2026-03-10] DI 感測器狀態
        [ObservableProperty] private bool _diCarouselHome;    // 刀盤歸位感測
        [ObservableProperty] private bool _diCarouselOut;     // 刀盤到位感測
        [ObservableProperty] private bool _diDrawbarClamp;    // 夾刀確認
        [ObservableProperty] private bool _diDrawbarUnclamp;  // 鬆刀確認

        // [2026-03-03] 程式刀具列表（ProgramTools 模式）
        public ObservableCollection<string> ProgramTools { get; } = new();

        // [2026-03-04] MonitorVM 參考（從 MainViewModel 傳入，用於讀取 GCodeText）
        public MonitorViewModel MonitorVM { get; set; }

        // [2026-03-06] 刀庫類型（從編譯時常數載入，供 View 切換佈局用）
        public AtcType CurrentAtcType => ACTIVE_ATC_TYPE; // [2026-03-06]
        public bool IsUmbrellaMode => ACTIVE_ATC_TYPE == AtcType.Umbrella; // [2026-03-06]
        public bool IsTurretMode => ACTIVE_ATC_TYPE == AtcType.Turret; // [2026-03-06]

        // [2026-03-06] 刀位狀態表（供 CarouselControl 綁定）
        public ObservableCollection<AtcSlotInfo> SlotInfos { get; } = new(); // [2026-03-06]

        // [2026-03-10] ATC 狀態定時輪詢（頁面活躍時每秒刷新角度/IO）
        private DispatcherTimer _atcPollTimer;
        private bool _isPolling = false;

        // [2026-03-10] ATC IO pin 號碼快取（從 MachineConfig.Atc 載入一次）
        private int _pinDoCarouselOut = 31;
        private int _pinDoCarouselHome = 30;
        private int _pinDoDrawbar = 29;
        private int _pinDoAirBlow = 28;
        private int _pinDoMotorFwd = 27;
        private int _pinDoMotorRev = 26;
        private int _pinDiCarouselHome = 31;
        private int _pinDiCarouselOut = 30;
        private int _pinDiDrawbarClamp = 29;
        private int _pinDiDrawbarUnclamp = 28;

        // [2026-03-06] 建構函式
        public AtcViewModel()
        {
            InitializeSlots(); // [2026-03-06]
            // [2026-03-10] 初始化 ATC 輪詢定時器（1 秒間隔，預設不啟動）
            _atcPollTimer = new DispatcherTimer { Interval = System.TimeSpan.FromSeconds(1) };
            _atcPollTimer.Tick += async (s, e) =>
            {
                if (_isPolling) return; // 防止重疊
                _isPolling = true;
                try { await RefreshAtcStatus(); }
                finally { _isPolling = false; }
            };
        }

        // [2026-03-10] 頁面進入時啟動輪詢 + 載入 IO pin 設定
        public async void StartPolling()
        {
            // [2026-03-10] 載入一次 AtcConfig 的 IO pin 號碼
            try
            {
                var cfg = await ConfigurationService.Instance.LoadConfigAsync();
                if (cfg?.Atc != null)
                {
                    _pinDoCarouselOut = cfg.Atc.DoCarouselOut;
                    _pinDoCarouselHome = cfg.Atc.DoCarouselHome;
                    _pinDoDrawbar = cfg.Atc.DoDrawbar;
                    _pinDoAirBlow = cfg.Atc.DoAirBlow;
                    _pinDoMotorFwd = cfg.Atc.DoMotorFwd;
                    _pinDoMotorRev = cfg.Atc.DoMotorRev;
                    _pinDiCarouselHome = cfg.Atc.DiCarouselHome;
                    _pinDiCarouselOut = cfg.Atc.DiCarouselOut;
                    _pinDiDrawbarClamp = cfg.Atc.DiDrawbarClamp;
                    _pinDiDrawbarUnclamp = cfg.Atc.DiDrawbarUnclamp;
                }
            }
            catch { /* 載入失敗用預設值 */ }

            if (!_atcPollTimer.IsEnabled)
                _atcPollTimer.Start();
        }

        // [2026-03-10] 頁面離開時停止輪詢
        public void StopPolling()
        {
            _atcPollTimer.Stop();
        }

        // [2026-03-09] CarouselAngle 變化時同步至 MachineStatus（供 HeaderBar 即時顯示）
        partial void OnCarouselAngleChanged(double value)
        {
            if (MachineStatus != null)
                MachineStatus.CarouselPosition = value;
        }

        // [2026-03-10] 初始化刀位表（預設 12 位，全空，由 RefreshAtcStatus 從後端 #4001~#4024 填入）
        private void InitializeSlots()
        {
            SlotInfos.Clear();
            int toolCount = 12;
            for (int i = 1; i <= toolCount; i++)
            {
                SlotInfos.Add(new AtcSlotInfo { SlotNumber = i, ToolNumber = 0 });
            }
        }

        // =====================================================================
        // 左面板 Commands（MANUAL ATC 模式）
        // =====================================================================

        // [2026-03-03] AIR BLAST：M7 吹氣清屑
        [RelayCommand]
        private async Task AirBlast()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: AIR BLAST (M7)");
            await MachineControlService.Instance.SendMdiCommandAsync("M7");
        }

        // [2026-03-03] RETR DUST BOOT：M64 P0 收回防塵罩
        [RelayCommand]
        private async Task RetrDustBoot()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: RETR DUST BOOT (M64 P0)");
            await MachineControlService.Instance.SendMdiCommandAsync("M64 P0");
        }

        // [2026-03-10] CLAMP TOOL：夾刀（M25 → M65 DO OFF → 彈簧夾緊）
        [RelayCommand]
        private async Task ClampTool()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: CLAMP TOOL");
            bool ok = await MachineControlService.Instance.AtcClampAsync();
            if (ok) IsDrawbarOn = false; // 夾刀 = DO OFF
        }

        // [2026-03-10] RELEASE TOOL：鬆刀（M24 → M64 DO ON → 氣壓推開）
        [RelayCommand]
        private async Task ReleaseTool()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: RELEASE TOOL");
            bool ok = await MachineControlService.Instance.AtcUnclampAsync();
            if (ok) IsDrawbarOn = true; // 鬆刀 = DO ON
        }

        // [2026-03-09] ORIENT SPINDLE：主軸定向（透過專用 API → M19）
        [RelayCommand]
        private async Task OrientSpindle()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: ORIENT SPINDLE");
            await MachineControlService.Instance.AtcOrientAsync();
        }

        // [2026-03-03] UNLOCK SPINDLE：M5 解除主軸定向
        [RelayCommand]
        private async Task UnlockSpindle()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: UNLOCK SPINDLE (M5)");
            await MachineControlService.Instance.SendMdiCommandAsync("M5");
        }

        // [2026-03-09] HEAD UP：Z 上升 10mm（方向鍵上）
        [RelayCommand]
        private async Task HeadUp()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: HEAD UP (G91 G0 Z10 G90)");
            await MachineControlService.Instance.SendMdiCommandAsync("G91 G0 Z10 G90");
        }

        // [2026-03-09] HEAD DOWN：Z 下降 10mm（方向鍵下）
        [RelayCommand]
        private async Task HeadDown()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: HEAD DOWN (G91 G0 Z-10 G90)");
            await MachineControlService.Instance.SendMdiCommandAsync("G91 G0 Z-10 G90");
        }

        // [2026-03-03] REF RACK DATA：重新讀取刀套資料（重載刀具表）
        [RelayCommand]
        private async Task RefRackData()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: REF RACK DATA (reload tool table)");
            // 送出 G10 L0 強制重載，或直接呼叫後端 API
            await MachineControlService.Instance.SendMdiCommandAsync("G10 L0");
        }

        // =====================================================================
        // 右面板 Commands（ATC AUTOMATIC CONTROL PANEL）
        // =====================================================================

        // [2026-03-03] LOAD SPINDLE：T{n} M6 自動換刀
        [RelayCommand]
        private async Task LoadSpindle()
        {
            if (ChangeToolNumber <= 0)
            {
                AlarmService.Instance.AddLog("WARN", "ATC LOAD: 請輸入有效的刀具號");
                return;
            }
            string cmd = $"T{ChangeToolNumber} M6";
            AlarmService.Instance.AddLog("INFO", $"ATC Load Spindle: {cmd}");
            await MachineControlService.Instance.SendMdiCommandAsync(cmd);
        }

        // [2026-03-03] UNLOAD SPINDLE：T0 M6 卸載刀具
        [RelayCommand]
        private async Task UnloadSpindle()
        {
            string cmd = "T0 M6";
            AlarmService.Instance.AddLog("INFO", $"ATC Unload Spindle: {cmd}");
            await MachineControlService.Instance.SendMdiCommandAsync(cmd);
        }

        // [2026-03-03] STORE TOOL IN RACK：T0 M6 存回刀套
        [RelayCommand]
        private async Task StoreToolInRack()
        {
            string cmd = "T0 M6";
            AlarmService.Instance.AddLog("INFO", $"ATC Store Tool In Rack: {cmd}");
            await MachineControlService.Instance.SendMdiCommandAsync(cmd);
        }

        // [2026-03-03] M6 G43：T{n} M6 G43 換刀 + 刀長補正
        [RelayCommand]
        private async Task M6G43()
        {
            if (ChangeToolNumber <= 0)
            {
                AlarmService.Instance.AddLog("WARN", "ATC M6 G43: 請輸入有效的刀具號");
                return;
            }
            string cmd = $"T{ChangeToolNumber} M6 G43";
            AlarmService.Instance.AddLog("INFO", $"ATC M6 G43: {cmd}");
            await MachineControlService.Instance.SendMdiCommandAsync(cmd);
        }

        // [2026-03-03] TOUCH OFF CURRENT TOOL：G10 L11 P{n} Z0 對刀
        [RelayCommand]
        private async Task TouchOffCurrentTool()
        {
            int toolNum = MachineStatus?.ToolNumber ?? 0;
            if (toolNum <= 0)
            {
                AlarmService.Instance.AddLog("WARN", "ATC TOUCH OFF: 目前無刀具在主軸");
                return;
            }
            string cmd = $"G10 L11 P{toolNum} Z0";
            AlarmService.Instance.AddLog("INFO", $"ATC Touch Off Current Tool: {cmd}");
            await MachineControlService.Instance.SendMdiCommandAsync(cmd);
        }

        // [2026-03-03] MDI 自由輸入送出
        [RelayCommand]
        private async Task SendMdi()
        {
            if (string.IsNullOrWhiteSpace(MdiInput)) return;
            AlarmService.Instance.AddLog("INFO", $"ATC MDI: {MdiInput}");
            await MachineControlService.Instance.SendMdiCommandAsync(MdiInput.Trim());
            MdiInput = "";
        }

        // [2026-03-04] 載入程式刀具：解析 MonitorVM.GCodeText 取得所有 T 號
        [RelayCommand]
        private void LoadProgramTools()
        {
            ProgramTools.Clear();
            if (MonitorVM == null || string.IsNullOrWhiteSpace(MonitorVM.GCodeText))
            {
                AlarmService.Instance.AddLog("WARN", "ATC: 無 G-Code 程式可解析");
                return;
            }
            var tools = GCodeParser.ExtractToolNumbers(MonitorVM.GCodeText);
            foreach (var t in tools)
                ProgramTools.Add($"T{t}");
            AlarmService.Instance.AddLog("INFO", $"ATC: 載入 {tools.Count} 把程式刀具");
        }

        // [2026-03-03] 模式切換（ManualAtc / ProgramTools）
        [RelayCommand]
        private void SwitchMode(string mode)
        {
            SelectedMode = mode;
            AlarmService.Instance.AddLog("INFO", $"ATC Mode: {mode}");
        }

        // =====================================================================
        // [2026-03-06] Carousel（斗笠式刀庫）專用命令
        // =====================================================================

        // [2026-03-09] ATC REV：刀盤逆時針旋轉一格（透過專用 API）
        [RelayCommand]
        private async Task AtcRev()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: REV");
            // [2026-03-09] 先送指令，成功後才更新角度（避免指令失敗但動畫已動）
            // [2026-03-10] 不再本地計算角度，由 RefreshAtcStatus 從後端讀取真實值
            await MachineControlService.Instance.AtcRevAsync();
            await RefreshAtcStatus();
        }

        // [2026-03-09] ATC FWD：刀盤順時針旋轉一格（透過專用 API）
        [RelayCommand]
        private async Task AtcFwd()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: FWD");
            // [2026-03-09] 先送指令，成功後才更新角度（避免指令失敗但動畫已動）
            // [2026-03-10] 不再本地計算角度，由 RefreshAtcStatus 從後端讀取真實值
            await MachineControlService.Instance.AtcFwdAsync();
            await RefreshAtcStatus();
        }

        // [2026-03-09] RETRACT ATC：收回刀盤
        [RelayCommand]
        private async Task RetractAtc()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: RETRACT");
            await MachineControlService.Instance.AtcRetractAsync();
            await RefreshAtcStatus();
        }

        // [2026-03-09] EXTEND ATC：伸出刀盤
        [RelayCommand]
        private async Task ExtendAtc()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: EXTEND");
            await MachineControlService.Instance.AtcExtendAsync();
            await RefreshAtcStatus();
        }

        // [2026-03-09] MOVE HEAD ABOVE CAROUSEL：Z 至淨空高度
        [RelayCommand]
        private async Task MoveHeadAboveCarousel()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: MOVE HEAD ABOVE CAROUSEL");
            await MachineControlService.Instance.AtcHeadUpAsync();
        }

        // [2026-03-09] MOVE TOOL TO CAROUSEL HEIGHT：Z 至換刀高度
        [RelayCommand]
        private async Task MoveToolToCarouselHeight()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: MOVE TOOL TO CAROUSEL HEIGHT");
            await MachineControlService.Instance.AtcHeadDownAsync();
        }

        // [2026-03-09] REF CAROUSEL：刀庫歸零
        [RelayCommand]
        private async Task RefCarousel()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: REF CAROUSEL");
            bool ok = await MachineControlService.Instance.AtcRefAsync();
            if (ok)
            {
                IsReferenced = true;
                await RefreshAtcStatus();
            }
            else
            {
                AlarmService.Instance.AddLog("ERROR", "ATC: REF CAROUSEL 失敗");
            }
        }

        // [2026-03-10] 設定刀位：將刀號寫入指定刀位（#4001~#4024）
        [RelayCommand]
        private async Task SetSlotTool()
        {
            if (SelectedSlotNumber < 1 || SelectedSlotNumber > SlotInfos.Count)
            {
                AlarmService.Instance.AddLog("WARN", $"ATC: 無效刀位號 {SelectedSlotNumber}");
                return;
            }
            if (SetSlotToolNumber < 0)
            {
                AlarmService.Instance.AddLog("WARN", $"ATC: 無效刀號 {SetSlotToolNumber}");
                return;
            }
            AlarmService.Instance.AddLog("INFO", $"ATC: 設定刀位 {SelectedSlotNumber} = T{SetSlotToolNumber}");
            bool ok = await MachineControlService.Instance.AtcSetSlotAsync(SelectedSlotNumber, SetSlotToolNumber);
            if (ok)
                await RefreshAtcStatus();
            else
                AlarmService.Instance.AddLog("ERROR", $"ATC: 設定刀位 {SelectedSlotNumber} 失敗");
        }

        // [2026-03-10] 清除刀位：將指定刀位的刀號設為 0（空位）
        [RelayCommand]
        private async Task ClearSlotTool()
        {
            if (SelectedSlotNumber < 1 || SelectedSlotNumber > SlotInfos.Count)
            {
                AlarmService.Instance.AddLog("WARN", $"ATC: 無效刀位號 {SelectedSlotNumber}");
                return;
            }
            AlarmService.Instance.AddLog("INFO", $"ATC: 清除刀位 {SelectedSlotNumber}");
            bool ok = await MachineControlService.Instance.AtcSetSlotAsync(SelectedSlotNumber, 0);
            if (ok)
                await RefreshAtcStatus();
            else
                AlarmService.Instance.AddLog("ERROR", $"ATC: 清除刀位 {SelectedSlotNumber} 失敗");
        }

        // [2026-03-10] 清除所有刀位：將全部刀位設為 0
        [RelayCommand]
        private async Task ClearAllSlots()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: 清除所有刀位");
            for (int i = 1; i <= SlotInfos.Count; i++)
            {
                await MachineControlService.Instance.AtcSetSlotAsync(i, 0);
            }
            await RefreshAtcStatus();
        }

        // [2026-03-09] ELECTRONIC TOOL SETTER
        [RelayCommand]
        private async Task ElectronicToolSetter()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: ELECTRONIC TOOL SETTER");
            await MachineControlService.Instance.SendMdiCommandAsync("o<tool_touch_off> call");
        }

        // =====================================================================
        // [2026-03-09] 即時狀態刷新（從後端讀取真實 IO + 刀位表）
        // =====================================================================
        public async Task RefreshAtcStatus()
        {
            var status = await MachineControlService.Instance.GetAtcStatusAsync();
            if (status == null) return;

            // [2026-03-09] 更新刀位號 + 角度
            if (status.CurrentPocket > 0)
            {
                CurrentPocket = status.CurrentPocket;
                AtcStatusText = $"刀位: {status.CurrentPocket}";
            }
            // [2026-03-10] Servo 模式：永遠用後端 encoder 真實角度（含 0°）；IO 模式：用 pocket 計算角度
            if (status.ControlMode == "SERVO")
            {
                CarouselAngle = status.CarouselAngle;
            }
            else if (status.CurrentPocket > 0)
            {
                int toolCount = SlotInfos.Count > 0 ? SlotInfos.Count : status.Pockets;
                if (toolCount > 0)
                    CarouselAngle = (status.CurrentPocket - 1) * (360.0 / toolCount);
            }

            // [2026-03-09] 更新刀位表（SlotTools: {slot: toolNum}）
            if (status.SlotTools != null)
            {
                foreach (var kvp in status.SlotTools)
                {
                    if (int.TryParse(kvp.Key, out int slot) && slot >= 1 && slot <= SlotInfos.Count)
                    {
                        SlotInfos[slot - 1].ToolNumber = kvp.Value;
                    }
                }
            }

            // [2026-03-10] 更新 DO 狀態（按鈕反白用）
            if (status.DO != null)
            {
                IsCarouselOut  = status.DO.TryGetValue(_pinDoCarouselOut.ToString(), out var v1) && v1;
                IsCarouselHome = status.DO.TryGetValue(_pinDoCarouselHome.ToString(), out var v2) && v2;
                IsDrawbarOn    = status.DO.TryGetValue(_pinDoDrawbar.ToString(), out var v3) && v3;
                IsAirBlowOn    = status.DO.TryGetValue(_pinDoAirBlow.ToString(), out var v4) && v4;
                IsMotorFwd     = status.DO.TryGetValue(_pinDoMotorFwd.ToString(), out var v5) && v5;
                IsMotorRev     = status.DO.TryGetValue(_pinDoMotorRev.ToString(), out var v6) && v6;
            }

            // [2026-03-10] 更新 DI 狀態（感測器指示用）
            if (status.DI != null)
            {
                DiCarouselHome    = status.DI.TryGetValue(_pinDiCarouselHome.ToString(), out var d1) && d1;
                DiCarouselOut     = status.DI.TryGetValue(_pinDiCarouselOut.ToString(), out var d2) && d2;
                DiDrawbarClamp    = status.DI.TryGetValue(_pinDiDrawbarClamp.ToString(), out var d3) && d3;
                DiDrawbarUnclamp  = status.DI.TryGetValue(_pinDiDrawbarUnclamp.ToString(), out var d4) && d4;
            }
        }
    }
}
