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

        // [2026-03-03] 程式刀具列表（ProgramTools 模式）
        public ObservableCollection<string> ProgramTools { get; } = new();

        // [2026-03-04] MonitorVM 參考（從 MainViewModel 傳入，用於讀取 GCodeText）
        public MonitorViewModel MonitorVM { get; set; }

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
    }
}
