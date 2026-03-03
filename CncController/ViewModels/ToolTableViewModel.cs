// [2026-03-03] 新增 ToolTableViewModel：TOOL 分頁 ViewModel（對齊 PB 版 TOOL 頁面）
//              刀具表 CRUD、LOAD/UNLOAD SPINDLE、M6 G43、TOUCH OFF、MDI 送出
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    public partial class ToolTableViewModel : ObservableObject
    {
        // [2026-03-03] 刀具表（DataGrid 綁定）
        public ObservableCollection<ToolEntry> ToolTable { get; } = new();

        // [2026-03-03] 選取的刀具列
        [ObservableProperty] private ToolEntry _selectedTool;

        // [2026-03-03] 即時機台狀態（刀具號/刀長/刀徑）
        [ObservableProperty] private MachineStatus _machineStatus;

        // [2026-03-03] TOOL CHANGE PANEL 的刀具號輸入
        [ObservableProperty] private int _changeToolNumber = 1;

        // [2026-03-03] MDI 輸入欄
        [ObservableProperty] private string _mdiInput = "";

        // [2026-03-03] 軸可見性（依 MachineType 動態顯示 DataGrid 欄位）
        // X/Y/Z 永遠顯示；A/B/C 依機台類型；U/V/W 預設隱藏（極少用到）
        [ObservableProperty] private bool _isAxisAEnabled;
        [ObservableProperty] private bool _isAxisBEnabled;
        [ObservableProperty] private bool _isAxisCEnabled;
        [ObservableProperty] private bool _isAxisUEnabled;
        [ObservableProperty] private bool _isAxisVEnabled;
        [ObservableProperty] private bool _isAxisWEnabled;

        // [2026-03-03] 選取刀具的註解（右側面板顯示）
        public string SelectedRemark => SelectedTool?.Remark ?? "";

        partial void OnSelectedToolChanged(ToolEntry value)
        {
            OnPropertyChanged(nameof(SelectedRemark));
            if (value != null)
                ChangeToolNumber = value.ToolNumber;
        }

        public ToolTableViewModel()
        {
            _ = AutoLoad();
        }

        private async Task AutoLoad()
        {
            try { await ReloadTable(); }
            catch { /* 離線時靜默失敗 */ }
        }

        // [2026-03-03] RELOAD TABLE：從後端讀取刀具表
        [RelayCommand]
        private async Task ReloadTable()
        {
            var tools = await MachineControlService.Instance.GetToolTableAsync();
            if (tools == null) return;
            ToolTable.Clear();
            foreach (var t in tools)
                ToolTable.Add(t);
            AlarmService.Instance.AddLog("INFO", $"Tool table reloaded ({tools.Count} tools)");
        }

        // [2026-03-03] SAVE TABLE：將刀具表寫入後端
        [RelayCommand]
        private async Task SaveTable()
        {
            var (allowed, reason) = MachineControlService.Instance.ValidateAction(
                MachineControlService.MachineAction.Mdi);
            if (!allowed)
            {
                AlarmService.Instance.AddLog("WARN", $"Save Tool Table Blocked: {reason}");
                return;
            }

            bool ok = await MachineControlService.Instance.SaveToolTableAsync(ToolTable.ToList());
            if (ok)
                AlarmService.Instance.AddLog("INFO", "Tool table saved");
            else
                AlarmService.Instance.AddLog("WARN", "Tool table save failed");
        }

        // [2026-03-03] ADD TOOL：新增空白刀具列
        [RelayCommand]
        private void AddTool()
        {
            int nextNum = ToolTable.Count > 0
                ? ToolTable.Max(t => t.ToolNumber) + 1
                : 1;
            var entry = new ToolEntry
            {
                ToolNumber = nextNum,
                Pocket = nextNum,
                Remark = ""
            };
            ToolTable.Add(entry);
            SelectedTool = entry;
            AlarmService.Instance.AddLog("INFO", $"Added Tool T{nextNum}");
        }

        // [2026-03-03] DELETE TOOL：刪除選取的刀具列
        [RelayCommand]
        private void DeleteTool()
        {
            if (SelectedTool == null)
            {
                AlarmService.Instance.AddLog("WARN", "DELETE: 請先選擇一把刀具");
                return;
            }
            int tNum = SelectedTool.ToolNumber;
            ToolTable.Remove(SelectedTool);
            SelectedTool = null;
            AlarmService.Instance.AddLog("INFO", $"Deleted Tool T{tNum}");
        }

        // [2026-03-03] LOAD SPINDLE：T{n} M6（換刀）
        [RelayCommand]
        private async Task LoadSpindle()
        {
            if (ChangeToolNumber <= 0)
            {
                AlarmService.Instance.AddLog("WARN", "LOAD SPINDLE: 請輸入有效的刀具號");
                return;
            }
            string cmd = $"T{ChangeToolNumber} M6";
            AlarmService.Instance.AddLog("INFO", $"Load Spindle: {cmd}");
            await MachineControlService.Instance.SendMdiCommandAsync(cmd);
        }

        // [2026-03-03] UNLOAD SPINDLE：T0 M6
        [RelayCommand]
        private async Task UnloadSpindle()
        {
            string cmd = "T0 M6";
            AlarmService.Instance.AddLog("INFO", $"Unload Spindle: {cmd}");
            await MachineControlService.Instance.SendMdiCommandAsync(cmd);
        }

        // [2026-03-03] M6 G43：換刀 + 啟用刀長補正
        [RelayCommand]
        private async Task M6G43()
        {
            if (ChangeToolNumber <= 0)
            {
                AlarmService.Instance.AddLog("WARN", "M6 G43: 請輸入有效的刀具號");
                return;
            }
            string cmd = $"T{ChangeToolNumber} M6 G43";
            AlarmService.Instance.AddLog("INFO", $"M6 G43: {cmd}");
            await MachineControlService.Instance.SendMdiCommandAsync(cmd);
        }

        // [2026-03-03] ELECTRONIC TOOL SETTER：預留（Probing subroutine）
        [RelayCommand]
        private async Task ElectronicToolSetter()
        {
            AlarmService.Instance.AddLog("INFO", "Electronic Tool Setter: (reserved for probing subroutine)");
            // 未來呼叫探測子程式：o<tool_setter> call
            await Task.CompletedTask;
        }

        // [2026-03-03] TOUCH OFF CURRENT TOOL：G10 L11 P{n} Z0（以目前位置設定刀長）
        [RelayCommand]
        private async Task TouchOffCurrentTool()
        {
            int toolNum = MachineStatus?.ToolNumber ?? 0;
            if (toolNum <= 0)
            {
                AlarmService.Instance.AddLog("WARN", "TOUCH OFF: 目前無刀具在主軸");
                return;
            }
            string cmd = $"G10 L11 P{toolNum} Z0";
            AlarmService.Instance.AddLog("INFO", $"Touch Off Current Tool: {cmd}");
            bool ok = await MachineControlService.Instance.SendMdiCommandAsync(cmd);
            if (ok)
            {
                await Task.Delay(200);
                await ReloadTable();
            }
        }

        // [2026-03-03] 自由 MDI 輸入
        [RelayCommand]
        private async Task SendMdi()
        {
            if (string.IsNullOrWhiteSpace(MdiInput)) return;
            AlarmService.Instance.AddLog("INFO", $"Tool MDI: {MdiInput}");
            await MachineControlService.Instance.SendMdiCommandAsync(MdiInput.Trim());
            MdiInput = "";
        }
    }
}
