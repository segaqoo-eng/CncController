// [2026-03-03] 新增 AtcViewModel：ATC 自動刀庫分頁 ViewModel
//              MANUAL ATC（手動按鈕）/ PROGRAM TOOLS（程式刀具列表）雙模式切換
//              所有指令透過 MachineControlService.SendMdiCommandAsync() 送出
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
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

        // [2026-03-06] 刀盤屬性
        [ObservableProperty] private double _carouselAngle = 0;        // [2026-03-06] 刀盤旋轉角度
        [ObservableProperty] private int _currentPocket = 1;            // [2026-03-06] 當前刀位號
        [ObservableProperty] private bool _isReferenced = false;        // [2026-03-06] 是否已歸零
        [ObservableProperty] private bool _isAtcBusy = false;           // [2026-03-06] 換刀進行中
        [ObservableProperty] private string _atcStatusText = "UN REFERENCED"; // [2026-03-06] 狀態文字

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

        // [2026-03-06] 建構函式
        public AtcViewModel()
        {
            InitializeSlots(); // [2026-03-06]
        }

        // [2026-03-06] 初始化刀位表（預設 12 位，部分有刀）
        private void InitializeSlots()
        {
            SlotInfos.Clear();
            int toolCount = 12; // 暫時寫死
            for (int i = 1; i <= toolCount; i++)
            {
                SlotInfos.Add(new AtcSlotInfo { SlotNumber = i, ToolNumber = 0 });
            }
            // 預設放幾把刀（測試用）
            if (SlotInfos.Count >= 3) { SlotInfos[0].ToolNumber = 1; SlotInfos[1].ToolNumber = 2; SlotInfos[2].ToolNumber = 3; }
            if (SlotInfos.Count >= 6) { SlotInfos[5].ToolNumber = 6; }
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

        // [2026-03-03] CLAMP TOOL：M64 P1 夾緊刀具
        [RelayCommand]
        private async Task ClampTool()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: CLAMP TOOL (M64 P1)");
            await MachineControlService.Instance.SendMdiCommandAsync("M64 P1");
        }

        // [2026-03-03] RELEASE TOOL：M65 P1 鬆開刀具
        [RelayCommand]
        private async Task ReleaseTool()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: RELEASE TOOL (M65 P1)");
            await MachineControlService.Instance.SendMdiCommandAsync("M65 P1");
        }

        // [2026-03-03] ORIENT SPINDLE：M19 主軸定向
        [RelayCommand]
        private async Task OrientSpindle()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: ORIENT SPINDLE (M19)");
            await MachineControlService.Instance.SendMdiCommandAsync("M19");
        }

        // [2026-03-03] UNLOCK SPINDLE：M5 解除主軸定向
        [RelayCommand]
        private async Task UnlockSpindle()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: UNLOCK SPINDLE (M5)");
            await MachineControlService.Instance.SendMdiCommandAsync("M5");
        }

        // [2026-03-03] HEAD UP：G91 G0 Z10 G90（Z 上升 10mm）
        [RelayCommand]
        private async Task HeadUp()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: HEAD UP (G91 G0 Z10 G90)");
            await MachineControlService.Instance.SendMdiCommandAsync("G91 G0 Z10 G90");
        }

        // [2026-03-03] HEAD DOWN：G91 G0 Z-10 G90（Z 下降 10mm）
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

        // [2026-03-06] ATC REV：刀盤逆時針旋轉一格
        [RelayCommand]
        private async Task AtcRev()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: REV (M12)"); // [2026-03-06]
            // [2026-03-06] 先觸發本地動畫（不等 MDI 回傳），再送後端命令
            int toolCount = SlotInfos.Count > 0 ? SlotInfos.Count : 12;
            CarouselAngle -= 360.0 / toolCount;
            CurrentPocket = CurrentPocket > 1 ? CurrentPocket - 1 : toolCount;
            await MachineControlService.Instance.SendMdiCommandAsync("M12");
        }

        // [2026-03-06] ATC FWD：刀盤順時針旋轉一格
        [RelayCommand]
        private async Task AtcFwd()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: FWD (M11)"); // [2026-03-06]
            // [2026-03-06] 先觸發本地動畫（不等 MDI 回傳），再送後端命令
            int toolCount = SlotInfos.Count > 0 ? SlotInfos.Count : 12;
            CarouselAngle += 360.0 / toolCount;
            CurrentPocket = CurrentPocket < toolCount ? CurrentPocket + 1 : 1;
            await MachineControlService.Instance.SendMdiCommandAsync("M11");
        }

        // [2026-03-06] RETRACT ATC：收回刀盤
        [RelayCommand]
        private async Task RetractAtc()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: RETRACT (o<retractatc> call)"); // [2026-03-06]
            await MachineControlService.Instance.SendMdiCommandAsync("o<retractatc> call"); // [2026-03-06]
        }

        // [2026-03-06] EXTEND ATC：伸出刀盤
        [RelayCommand]
        private async Task ExtendAtc()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: EXTEND (o<extendatc> call)"); // [2026-03-06]
            await MachineControlService.Instance.SendMdiCommandAsync("o<extendatc> call"); // [2026-03-06]
        }

        // [2026-03-06] MOVE HEAD ABOVE CAROUSEL：Z 至淨空高度
        [RelayCommand]
        private async Task MoveHeadAboveCarousel()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: MOVE HEAD ABOVE CAROUSEL"); // [2026-03-06]
            await MachineControlService.Instance.SendMdiCommandAsync("o<move_head_above_carousel> call"); // [2026-03-06]
        }

        // [2026-03-06] MOVE TOOL TO CAROUSEL HEIGHT：Z 至換刀高度
        [RelayCommand]
        private async Task MoveToolToCarouselHeight()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: MOVE TOOL TO CAROUSEL HEIGHT"); // [2026-03-06]
            await MachineControlService.Instance.SendMdiCommandAsync("o<move_tool_to_carousel_height> call"); // [2026-03-06]
        }

        // [2026-03-06] REF CAROUSEL：刀庫歸零
        [RelayCommand]
        private async Task RefCarousel()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: REF CAROUSEL (M13)"); // [2026-03-06]
            await MachineControlService.Instance.SendMdiCommandAsync("M13"); // [2026-03-06]
            IsReferenced = true; // [2026-03-06]
            CurrentPocket = 1; // [2026-03-06]
            AtcStatusText = "POCKET: 1"; // [2026-03-06]
        }

        // [2026-03-06] ELECTRONIC TOOL SETTER
        [RelayCommand]
        private async Task ElectronicToolSetter()
        {
            AlarmService.Instance.AddLog("INFO", "ATC: ELECTRONIC TOOL SETTER"); // [2026-03-06]
            await MachineControlService.Instance.SendMdiCommandAsync("o<tool_touch_off> call"); // [2026-03-06]
        }
    }
}
