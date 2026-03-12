using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using CncController.Services;

namespace CncController.ViewModels
{
    // [2026-03-12] 從 MainViewModel.cs 拆分：加工循環控制（Cycle/Feed/Coolant/Override）
    public partial class MainViewModel
    {
        // 暫存目前載入的檔名
        private string _loadedFileName = null;

        [ObservableProperty]
        private bool _isFloodOn;

        // [2026-02-23] MIST 噴霧冷卻狀態
        [ObservableProperty]
        private bool _isMistOn;

        // [2026-02-24] Single Block 模式
        [ObservableProperty]
        private bool _isSingleBlock;

        // [2026-03-04] Block Delete / Optional Stop 開關
        [ObservableProperty] private bool _isBlockDelete;
        [ObservableProperty] private bool _isOptionalStop;

        [RelayCommand]
        private async Task ToggleFlood()
        {
            string cmd = IsFloodOn ? "M9" : "M8";
            bool success = await MachineControlService.Instance.SendMdiCommandAsync(cmd);
            if (success)
                IsFloodOn = !IsFloodOn;
        }

        // [2026-02-23] ToggleMist：M7=噴霧開，M9=全部冷卻關
        [RelayCommand]
        private async Task ToggleMist()
        {
            string cmd = IsMistOn ? "M9" : "M7";
            bool success = await MachineControlService.Instance.SendMdiCommandAsync(cmd);
            if (success)
            {
                IsMistOn = !IsMistOn;
                if (!IsMistOn) IsFloodOn = false;
            }
        }

        // [2026-02-24] IsSingleBlock 模式下改用 StepProgramAsync 單節執行
        [RelayCommand]
        private async Task CycleStart()
        {
            if (!CanExecuteMotion()) return;

            if (IsSingleBlock)
            {
                await MachineControlService.Instance.StepProgramAsync();
            }
            else
            {
                _loadedFileName = MonitorVM.CurrentFileName;
                await MachineControlService.Instance.CycleStartAsync(_loadedFileName);
            }

            if (Status.InterpState == "IDLE")
            {
                _cycleStartTime = DateTime.Now;
                CycleTimeDisplay = "00:00:00";
            }
        }

        [RelayCommand] private async Task FeedHold() => await MachineControlService.Instance.FeedHoldAsync();
        [RelayCommand] private async Task Stop() => await MachineControlService.Instance.StopAsync();

        // [2026-03-03] 切換任務模式（MAN/AUTO/MDI）
        [RelayCommand]
        private async Task SetMode(string mode)
        {
            await MachineControlService.Instance.SetTaskModeAsync(mode);
        }

        // [2026-02-24] 切換 Single Block 模式
        [RelayCommand]
        private void ToggleSingleBlock()
        {
            IsSingleBlock = !IsSingleBlock;
            AlarmService.Instance.AddLog("INFO", $"Single Block: {(IsSingleBlock ? "ON" : "OFF")}");
        }

        // [2026-03-04] 切換 Block Delete 模式
        [RelayCommand]
        private async Task ToggleBlockDelete()
        {
            bool newVal = !IsBlockDelete;
            IsBlockDelete = newVal;
            Status.IsBlockDelete = newVal;
            AlarmService.Instance.AddLog("INFO", $"Block Delete: {(newVal ? "ON" : "OFF")}");
            await MachineControlService.Instance.SetBlockDeleteAsync(newVal);
        }

        // [2026-03-04] 切換 Optional Stop (M01) 模式
        [RelayCommand]
        private async Task ToggleOptionalStop()
        {
            bool newVal = !IsOptionalStop;
            IsOptionalStop = newVal;
            Status.IsOptionalStop = newVal;
            AlarmService.Instance.AddLog("INFO", $"Optional Stop (M01): {(newVal ? "ON" : "OFF")}");
            await MachineControlService.Instance.SetOptionalStopAsync(newVal);
        }

        // [2026-02-24] 增減 Feed Override
        [RelayCommand]
        private async Task AdjustFeedOverride(string deltaStr)
        {
            if (!double.TryParse(deltaStr, out double delta)) return;
            double newValue = Math.Clamp(Status.FeedOverride + delta, 0, 200);
            await MachineControlService.Instance.SetFeedOverrideAsync(newValue);
        }

        // [2026-02-24] 增減 Spindle Override
        [RelayCommand]
        private async Task AdjustSpindleOverride(string deltaStr)
        {
            if (!double.TryParse(deltaStr, out double delta)) return;
            double newValue = Math.Clamp(Status.SpindleOverride + delta, 0, 200);
            await MachineControlService.Instance.SetSpindleOverrideAsync(newValue);
        }

        // [2026-03-05] 清除已載入的 G-Code
        [RelayCommand]
        private async Task ClearProgram()
        {
            _loadedFileName = null;
            AlarmService.Instance.AddLog("INFO", "Program cleared");
            await Task.CompletedTask;
        }

        // [2026-03-05] 重置 Feed Override 為 100%
        [RelayCommand]
        private async Task ResetFeedOverride()
        {
            await MachineControlService.Instance.SetFeedOverrideAsync(100);
        }

        // [2026-03-05] 重置 Spindle Override 為 100%
        [RelayCommand]
        private async Task ResetSpindleOverride()
        {
            await MachineControlService.Instance.SetSpindleOverrideAsync(100);
        }

        // [2026-03-05] 重置 Velocity Override 為 100%（暫靜態）
        [RelayCommand]
        private void ResetVelocityOverride()
        {
            VelocityOverride = 100.0;
        }

        // [2026-03-05] 重置 Rapid Override 為 100%（暫靜態）
        [RelayCommand]
        private void ResetRapidOverride()
        {
            RapidOverride = 100.0;
        }

        // Reload 指令（重載當前檔案）
        [RelayCommand]
        private async Task ReloadCommand()
        {
            if (!string.IsNullOrEmpty(_loadedFileName))
            {
                AlarmService.Instance.AddLog("INFO", $"Reloading {_loadedFileName}...");
            }
        }
    }
}
