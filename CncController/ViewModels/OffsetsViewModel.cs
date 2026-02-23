using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
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

        public ObservableCollection<WorkOffsetRow> OffsetTable { get; } = new()
        {
            new() { Name = "G54" }, new() { Name = "G55" }, new() { Name = "G56" },
            new() { Name = "G57" }, new() { Name = "G58" }, new() { Name = "G59" },
        };

        public OffsetsViewModel()
        {
            // 開啟頁面時自動從後端載入 offset 值
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

        [RelayCommand]
        private void SetToZero()
        {
            // TODO: G10 L20 P? X0 Y0 Z0
            AlarmService.Instance.AddLog("INFO", "SetToZero: TODO");
        }

        [RelayCommand]
        private void ClearAll()
        {
            // TODO: 清除所有 offset
            AlarmService.Instance.AddLog("INFO", "ClearAll Offsets: TODO");
        }

        [RelayCommand]
        private void SaveTable()
        {
            // TODO: 儲存 offset 至後端
            AlarmService.Instance.AddLog("INFO", "SaveTable: TODO");
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
