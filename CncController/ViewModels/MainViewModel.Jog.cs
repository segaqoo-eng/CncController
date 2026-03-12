using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CncController.Services;

namespace CncController.ViewModels
{
    // [2026-03-12] 從 MainViewModel.cs 拆分：JOG 手動移動 + 主軸控制
    public partial class MainViewModel
    {
        // === JOG 設定 ===
        [ObservableProperty] private double _jogFeedrate = 1500.0;
        [ObservableProperty] private double _jogStepDistance = 0;
        // [2026-03-05] JOG 速度百分比（0~100%），對齊 PB 版 D_5
        [ObservableProperty] private double _jogSpeedPercent = 100.0;
        // [2026-03-05] JOG 主軸轉速 RPM，對齊 PB 版 D_5
        [ObservableProperty] private double _jogSpindleRpm = 300.0;

        // [2026-03-05] Override 靜態值（V/R 暫無後端連動），對齊 PB 版 D_4
        [ObservableProperty] private double _velocityOverride = 100.0;
        [ObservableProperty] private double _rapidOverride = 100.0;
        private readonly HashSet<int> _activeJogAxes = new();

        [RelayCommand]
        private async Task JogStart(string args)
        {
            if (!CanExecuteMotion()) return;
            if (string.IsNullOrEmpty(args)) return;
            var parts = args.Split(',');

            if (parts.Length == 2 && int.TryParse(parts[0], out int axis) && double.TryParse(parts[1], out double dirSign))
            {
                _activeJogAxes.Add(axis);

                double finalSpeed = Math.Abs(JogFeedrate) * (dirSign > 0 ? 1 : -1);
                double distance = JogStepDistance > 0 ? JogStepDistance : 0;

                await MachineControlService.Instance.JogAsync(axis, finalSpeed, distance);
            }
        }

        [RelayCommand]
        private async Task JogStop(string axisStr)
        {
            if (int.TryParse(axisStr, out int axis))
            {
                if (!_activeJogAxes.Contains(axis))
                    return;

                _activeJogAxes.Remove(axis);

                if (JogStepDistance == 0)
                {
                    await MachineControlService.Instance.JogStopAsync(axis);
                }
            }
        }

        [RelayCommand]
        private void SetJogMode(string value)
        {
            if (double.TryParse(value, out double dist))
            {
                JogStepDistance = dist;
            }
        }

        // [2026-03-05] 主軸正轉（FWD），對齊 PB 版 D_5
        [RelayCommand]
        private async Task SpindleFwd()
        {
            if (!CanExecuteMotion()) return;
            await MachineControlService.Instance.SendMdiCommandAsync($"M3 S{JogSpindleRpm}");
        }

        // [2026-03-05] 主軸反轉（REV），對齊 PB 版 D_5
        [RelayCommand]
        private async Task SpindleRev()
        {
            if (!CanExecuteMotion()) return;
            await MachineControlService.Instance.SendMdiCommandAsync($"M4 S{JogSpindleRpm}");
        }

        // [2026-03-05] 主軸停止（STOP），對齊 PB 版 D_5
        [RelayCommand]
        private async Task SpindleStop()
        {
            if (!CanExecuteMotion()) return;
            await MachineControlService.Instance.SendMdiCommandAsync("M5");
        }
    }
}
