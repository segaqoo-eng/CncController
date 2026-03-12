using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using System.Windows.Media;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    // [2026-03-12] 從 MainViewModel.cs 拆分：安全控制（電源/急停/重連/全機停止）
    public partial class MainViewModel
    {
        // [安全] 統一運動指令前置檢查：IsEstop 與 IsPower 雙重驗證
        private bool CanExecuteMotion()
        {
            if (IsEstop)
            {
                AlarmService.Instance.AddLog(LogType.Warning, "Motion blocked: E-Stop active");
                return false;
            }
            if (!IsPower)
            {
                AlarmService.Instance.AddLog(LogType.Warning, "Motion blocked: Machine power off");
                return false;
            }
            return true;
        }

        [RelayCommand]
        private async Task TogglePower()
        {
            if (IsEstop)
            {
                AlarmService.Instance.AddLog(LogType.Warning, "Cannot toggle Power while E-STOP is active");
                return;
            }
            await MachineControlService.Instance.ResetMachineAsync();
        }

        // [2026-03-04] RETRY 連線：重置失敗計數 + 重新進入寬限期 5 秒
        [RelayCommand]
        private async Task RetryConnection()
        {
            MachineControlService.Instance.ResetFailCount();
            IsStartingUp = true;
            ShowRetryButton = false;
            SystemStatus = "Reconnecting...";
            SystemStatusColor = Colors.Yellow;
            AlarmService.Instance.AddLog("INFO", "User triggered RETRY connection");
            await Task.Delay(5000);
            IsStartingUp = false;
        }

        // [2026-03-04] RE-SCAN 硬體：重新執行硬體掃描與驗證
        [RelayCommand]
        private async Task RescanHardware()
        {
            SystemStatus = "Re-scanning Hardware...";
            SystemStatusColor = Colors.Yellow;
            IsSystemReady = false;
            AlarmService.Instance.AddLog("INFO", "User triggered RE-SCAN hardware");
            await AutoValidateHardware();
        }

        [RelayCommand]
        private async Task ToggleEstop()
        {
            if (IsEstop)
            {
                await MachineControlService.Instance.ResetMachineAsync();
            }
            else
            {
                // [安全] 樂觀更新：立即設定 IsEstop=true
                IsEstop       = true;
                IsSystemReady = false;
                await MachineControlService.Instance.TriggerEstopAsync();
            }
        }

        // [2026-03-06] ESC = 全機停止（abort）：不分狀態，直接送 cnc_cmd.abort() + 清除警報
        [RelayCommand]
        private async Task EmergencyAbort()
        {
            AlarmService.Instance.AddLog("WARN", "ESC 全機停止");
            await MachineControlService.Instance.StopAsync();
            AlarmService.Instance.ClearActiveAlarms();
            UpdateHeaderStatus();
        }

        [RelayCommand]
        private void ClearAlarms()
        {
            AlarmService.Instance.ClearActiveAlarms();
            UpdateHeaderStatus();
        }
    }
}
