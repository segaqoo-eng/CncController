// [2026-02-23] 新增 OffsetsViewModel：G54–G59 工件座標系管理頁面的 ViewModel
//              WorkOffsetRow：每列 WCS 的 X/Y/Z 可觀察資料物件
//              SelectOffsetCommand：送 MDI 指令切換 Active WCS
//              ReloadTableCommand：從後端 /v2/offsets 讀取最新 offset 值
// [2026-02-24] 實作 SetToZeroAxis（G10 L20）、ClearSelected、ClearAll、SaveTable
//              新增 MachineStatus 屬性供右欄即時座標綁定
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    public partial class WorkOffsetRow : ObservableObject
    {
        public string Name { get; set; } = "";
        [ObservableProperty] private double _x;
        [ObservableProperty] private double _y;
        [ObservableProperty] private double _z;
        [ObservableProperty] private double _a;
        [ObservableProperty] private double _b;
        [ObservableProperty] private double _c;
    }

    public partial class OffsetsViewModel : ObservableObject
    {
        [ObservableProperty] private string _activeOffset = "G54";
        [ObservableProperty] private WorkOffsetRow _selectedRow;

        // [2026-02-24] 新增：引用 MachineStatus 讓右欄可綁定即時機台座標（MC Current / WC）
        [ObservableProperty] private MachineStatus _machineStatus;

        public ObservableCollection<WorkOffsetRow> OffsetTable { get; } = new()
        {
            new() { Name = "G54" }, new() { Name = "G55" }, new() { Name = "G56" },
            new() { Name = "G57" }, new() { Name = "G58" }, new() { Name = "G59" },
        };

        // [2026-02-24] G-code 名稱 → G10 L20 的 P 號對照（G54=P1, G55=P2, ..., G59=P6）
        private static readonly Dictionary<string, int> WcsToPNumber = new()
        {
            ["G54"] = 1, ["G55"] = 2, ["G56"] = 3,
            ["G57"] = 4, ["G58"] = 5, ["G59"] = 6,
        };

        public OffsetsViewModel()
        {
            // [2026-02-23] 開啟頁面時自動從後端載入 offset 值（離線時靜默失敗）
            _ = AutoLoad();
        }

        private async Task AutoLoad()
        {
            try { await ReloadTable(); }
            catch { /* 離線時靜默失敗，等使用者手動 RELOAD */ }
        }

        [RelayCommand]
        private async Task SelectOffset(string g)
        {
            if (string.IsNullOrEmpty(g)) return;
            var (allowed, reason) = MachineControlService.Instance.ValidateAction(
                MachineControlService.MachineAction.Mdi);
            if (!allowed)
            {
                AlarmService.Instance.AddLog("WARN", $"Offset Blocked: {reason}");
                return;
            }
            bool ok = await MachineControlService.Instance.SendMdiCommandAsync(g);
            if (ok)
            {
                ActiveOffset = g;
                AlarmService.Instance.AddLog("INFO", $"Active Coord: {g}");
            }
        }

        // [2026-02-24] 實作 SetToZeroAxis：透過 G10 L20 P<n> 將指定軸歸零
        //   axis = "X", "Y", "Z" 或 "ALL"
        //   G10 L20：以目前位置為基準設定 WCS offset，使工件座標變為指定值（此處設 0）
        [RelayCommand]
        private async Task SetToZeroAxis(string axis)
        {
            if (SelectedRow == null)
            {
                AlarmService.Instance.AddLog("WARN", "SET TO ZERO: 請先選擇一個座標系");
                return;
            }

            if (!WcsToPNumber.TryGetValue(SelectedRow.Name, out int pNum))
            {
                AlarmService.Instance.AddLog("WARN", $"SET TO ZERO: 無法辨識座標系 {SelectedRow.Name}");
                return;
            }

            var (allowed, reason) = MachineControlService.Instance.ValidateAction(
                MachineControlService.MachineAction.Mdi);
            if (!allowed)
            {
                AlarmService.Instance.AddLog("WARN", $"SET TO ZERO Blocked: {reason}");
                return;
            }

            // 組合 G10 L20 指令
            string axesPart = axis switch
            {
                "X" => "X0",
                "Y" => "Y0",
                "Z" => "Z0",
                "ALL" => "X0 Y0 Z0",
                _ => ""
            };

            if (string.IsNullOrEmpty(axesPart)) return;

            string mdiCmd = $"G10 L20 P{pNum} {axesPart}";
            bool ok = await MachineControlService.Instance.SendMdiCommandAsync(mdiCmd);
            if (ok)
            {
                AlarmService.Instance.AddLog("INFO", $"Set {SelectedRow.Name} {axesPart} to zero");
                // 成功後自動重新載入 offset 表
                await ReloadTable();
            }
        }

        // [2026-02-24] 實作 ClearSelected：將選定座標系全部六軸 offset 歸零（G10 L2 P<n> X0 Y0 Z0 A0 B0 C0）
        //   G10 L2：直接設定 WCS offset 為指定絕對值（0 = 清除）
        [RelayCommand]
        private async Task ClearSelected()
        {
            if (SelectedRow == null)
            {
                AlarmService.Instance.AddLog("WARN", "CLEAR: 請先選擇一個座標系");
                return;
            }

            if (!WcsToPNumber.TryGetValue(SelectedRow.Name, out int pNum)) return;

            var (allowed, reason) = MachineControlService.Instance.ValidateAction(
                MachineControlService.MachineAction.Mdi);
            if (!allowed)
            {
                AlarmService.Instance.AddLog("WARN", $"CLEAR Blocked: {reason}");
                return;
            }

            string mdiCmd = $"G10 L2 P{pNum} X0 Y0 Z0 A0 B0 C0";
            bool ok = await MachineControlService.Instance.SendMdiCommandAsync(mdiCmd);
            if (ok)
            {
                AlarmService.Instance.AddLog("INFO", $"Cleared {SelectedRow.Name} offsets");
                await ReloadTable();
            }
        }

        // [2026-02-24] 實作 ClearAll：將 G54–G59 全部六組 offset 歸零
        [RelayCommand]
        private async Task ClearAll()
        {
            var (allowed, reason) = MachineControlService.Instance.ValidateAction(
                MachineControlService.MachineAction.Mdi);
            if (!allowed)
            {
                AlarmService.Instance.AddLog("WARN", $"CLEAR ALL Blocked: {reason}");
                return;
            }

            foreach (var kv in WcsToPNumber)
            {
                string mdiCmd = $"G10 L2 P{kv.Value} X0 Y0 Z0 A0 B0 C0";
                bool ok = await MachineControlService.Instance.SendMdiCommandAsync(mdiCmd);
                if (!ok)
                {
                    AlarmService.Instance.AddLog("WARN", $"CLEAR ALL: {kv.Key} 指令失敗");
                    break;
                }
            }
            AlarmService.Instance.AddLog("INFO", "All WCS offsets cleared");
            await ReloadTable();
        }

        // [2026-02-24] 實作 SaveTable：透過 G10 L2 將表格中的值寫入各 WCS
        //   將使用者在 DataGrid 中編輯的值透過 MDI 回寫至 LinuxCNC
        [RelayCommand]
        private async Task SaveTable()
        {
            var (allowed, reason) = MachineControlService.Instance.ValidateAction(
                MachineControlService.MachineAction.Mdi);
            if (!allowed)
            {
                AlarmService.Instance.AddLog("WARN", $"SAVE Blocked: {reason}");
                return;
            }

            foreach (var row in OffsetTable)
            {
                if (!WcsToPNumber.TryGetValue(row.Name, out int pNum)) continue;
                string mdiCmd = $"G10 L2 P{pNum} X{row.X:F4} Y{row.Y:F4} Z{row.Z:F4} A{row.A:F4} B{row.B:F4} C{row.C:F4}";
                bool ok = await MachineControlService.Instance.SendMdiCommandAsync(mdiCmd);
                if (!ok)
                {
                    AlarmService.Instance.AddLog("WARN", $"SAVE: {row.Name} 指令失敗");
                    break;
                }
            }
            AlarmService.Instance.AddLog("INFO", "Offset table saved to LinuxCNC");
            await ReloadTable();
        }

        [RelayCommand]
        private async Task ReloadTable()
        {
            var data = await MachineControlService.Instance.GetOffsetsAsync();
            if (data == null) return;
            foreach (var row in OffsetTable)
            {
                if (data.TryGetValue(row.Name, out var vals))
                {
                    row.X = vals.GetValueOrDefault("X");
                    row.Y = vals.GetValueOrDefault("Y");
                    row.Z = vals.GetValueOrDefault("Z");
                    row.A = vals.GetValueOrDefault("A");
                    row.B = vals.GetValueOrDefault("B");
                    row.C = vals.GetValueOrDefault("C");
                }
            }
            AlarmService.Instance.AddLog("INFO", "Offset table reloaded.");
        }
    }
}
