using CommunityToolkit.Mvvm.Input;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CncController.Services;
using CncController.Models;

namespace CncController.ViewModels
{
    // [2026-03-12] 從 MainViewModel.cs 拆分：DRO 歸零 / 原點復歸 / 機台類型連動
    public partial class MainViewModel
    {
        // [2026-02-23] HomeAll：呼叫 HomeAsync(-1) 全軸回原點
        [RelayCommand]
        private async Task HomeAll()
        {
            if (IsEstop)
            {
                AlarmService.Instance.AddLog("WARN", "Home Blocked: E-Stop active");
                return;
            }
            if (!IsPower)
            {
                AlarmService.Instance.AddLog("WARN", "Home Blocked: Machine power off");
                return;
            }
            // [2026-02-24] 樂觀更新：立即設為未復歸
            Status.IsXHomed = false; Status.IsYHomed = false; Status.IsZHomed = false;
            Status.IsAHomed = false; Status.IsBHomed = false; Status.IsCHomed = false;
            Status.IsAllHomed = false;
            AlarmService.Instance.AddLog("INFO", "Homing All Axes...");
            bool ok = await MachineControlService.Instance.HomeAsync(-1);
            if (!ok)
                AlarmService.Instance.AddLog("WARN", "Home command failed");
        }

        // [2026-02-24] GoToZero：移至當前 G5X 工件座標零點
        [RelayCommand]
        private async Task GoToZero()
        {
            if (!CanExecuteMotion()) return;
            AlarmService.Instance.AddLog("INFO", $"Go To Zero (Work): G0 X0 Y0 Z0 [{Status.ActiveCoordSystem}]");
            await MachineControlService.Instance.SendMdiCommandAsync("G0 X0 Y0 Z0");
        }

        // [2026-02-24] GoToHome：移至機械零點
        [RelayCommand]
        private async Task GoToHome()
        {
            if (!CanExecuteMotion()) return;
            AlarmService.Instance.AddLog("INFO", "Go To Home (Machine): G53 G0 X0 Y0 Z0");
            await MachineControlService.Instance.SendMdiCommandAsync("G53 G0 X0 Y0 Z0");
        }

        // [2026-02-24] GoToG30：移至 G30 第二參考點
        [RelayCommand]
        private async Task GoToG30()
        {
            if (!CanExecuteMotion()) return;
            AlarmService.Instance.AddLog("INFO", "Go To G30 Reference Point");
            await MachineControlService.Instance.SendMdiCommandAsync("G30");
        }

        // [2026-02-24] DRO 單軸歸零：G10 L20 P<wcs> <axis>0
        [RelayCommand]
        private async Task DroZeroAxis(string axisName)
        {
            if (!CanExecuteMotion()) return;
            var wcsMap = new Dictionary<string, int>
                { {"G54",1}, {"G55",2}, {"G56",3}, {"G57",4}, {"G58",5}, {"G59",6},
                  {"G59.1",7}, {"G59.2",8}, {"G59.3",9} };
            if (!wcsMap.TryGetValue(Status.ActiveCoordSystem, out int pNum)) return;
            string cmd = $"G10 L20 P{pNum} {axisName}0";
            AlarmService.Instance.AddLog("INFO", $"DRO Zero {axisName}: {cmd}");
            await MachineControlService.Instance.SendMdiCommandAsync(cmd);
        }

        // [2026-02-24] DRO 全軸歸零
        [RelayCommand]
        private async Task DroZeroAll()
        {
            if (!CanExecuteMotion()) return;
            var wcsMap = new Dictionary<string, int>
                { {"G54",1}, {"G55",2}, {"G56",3}, {"G57",4}, {"G58",5}, {"G59",6},
                  {"G59.1",7}, {"G59.2",8}, {"G59.3",9} };
            if (!wcsMap.TryGetValue(Status.ActiveCoordSystem, out int pNum)) return;
            var axes = LastValidatedConfig?.GetEnabledAxes() ?? new() { "X", "Y", "Z" };
            string axesPart = string.Join(" ", axes.Select(a => $"{a}0"));
            string cmd = $"G10 L20 P{pNum} {axesPart}";
            AlarmService.Instance.AddLog("INFO", $"DRO Zero All: {cmd}");
            await MachineControlService.Instance.SendMdiCommandAsync(cmd);
        }

        // [2026-02-24] DRO 單軸原點復歸（樂觀更新）
        [RelayCommand]
        private async Task RefAxis(string axisName)
        {
            var map = new Dictionary<string, int>
                { {"X",0}, {"Y",1}, {"Z",2}, {"A",3}, {"B",4}, {"C",5} };
            if (!map.TryGetValue(axisName, out int idx)) return;
            SetAxisHomed(axisName, false);
            Status.IsAllHomed = false;
            AlarmService.Instance.AddLog("INFO", $"REF {axisName}: HomeAsync({idx})");
            await MachineControlService.Instance.HomeAsync(idx);
        }

        // [2026-02-24] 輔助：依軸名設定 Homed 狀態
        private void SetAxisHomed(string axis, bool value)
        {
            switch (axis)
            {
                case "X": Status.IsXHomed = value; break;
                case "Y": Status.IsYHomed = value; break;
                case "Z": Status.IsZHomed = value; break;
                case "A": Status.IsAHomed = value; break;
                case "B": Status.IsBHomed = value; break;
                case "C": Status.IsCHomed = value; break;
            }
        }

        // [2026-02-24] 機台類型即時連動
        public void ApplyMachineType(MachineType machineType, List<string> enabledAxes)
        {
            IsAxisAEnabled = enabledAxes.Contains("A");
            IsAxisBEnabled = enabledAxes.Contains("B");
            IsAxisCEnabled = enabledAxes.Contains("C");

            OffsetsVM.EnabledAxes = enabledAxes;
            OffsetsVM.IsAxisAEnabled = IsAxisAEnabled;
            OffsetsVM.IsAxisBEnabled = IsAxisBEnabled;
            OffsetsVM.IsAxisCEnabled = IsAxisCEnabled;

            // [2026-03-03] Tool Tab 欄位可見性同步
            ToolTableVM.IsAxisAEnabled = IsAxisAEnabled;
            ToolTableVM.IsAxisBEnabled = IsAxisBEnabled;
            ToolTableVM.IsAxisCEnabled = IsAxisCEnabled;

            AlarmService.Instance.AddLog("INFO", $"MachineType changed: {machineType} → axes={string.Join(",", enabledAxes)}");
        }
    }
}
