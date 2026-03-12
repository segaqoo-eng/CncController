using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using CncController.Services;
using CncController.Models;

namespace CncController.ViewModels
{
    // [2026-03-12] 從 MainViewModel.cs 拆分：加工統計 / 斷電續切 / 語言主題切換
    public partial class MainViewModel
    {
        // [2026-03-12] 加工統計顯示
        [ObservableProperty] private string _totalMachiningTimeDisplay = "--";
        [ObservableProperty] private int _totalCycleCount;
        [ObservableProperty] private string _estimatedRemainingDisplay = "--";
        [ObservableProperty] private double _machiningProgressPercent;

        // [2026-03-12] 語言/主題選單打勾狀態
        [ObservableProperty] private bool _isLangZhTW = true;
        [ObservableProperty] private bool _isLangEnUS = false;
        [ObservableProperty] private bool _isThemeDefault = true;
        [ObservableProperty] private bool _isThemeIndustrial = false;
        [ObservableProperty] private bool _isThemeCyber = false;

        // [2026-02-23] ExitApp：HeaderBar File 選單 EXIT
        [RelayCommand]
        private void ExitApp()
        {
            AlarmService.Instance.AddLog("INFO", "User requested application exit.");
            System.Windows.Application.Current.Shutdown();
        }

        // [2026-03-12] 開啟主軸暖機對話框
        [RelayCommand]
        private void OpenSpindleWarmup()
        {
            var dialog = new Views.Windows.SpindleWarmupWindow
            {
                Owner = System.Windows.Application.Current.MainWindow
            };
            dialog.ShowDialog();
        }

        // [2026-03-12] 多語言切換 + 持久化 + 打勾
        [RelayCommand]
        private void SwitchLanguage(string culture)
        {
            LocalizationService.Instance.SwitchLanguage(culture);
            AppSettings.Instance.UpdateLanguage(culture);
            IsLangZhTW = culture == "zh-TW";
            IsLangEnUS = culture == "en-US";
            AlarmService.Instance.AddLog("INFO", $"Language switched to {culture}");
        }

        // [2026-03-12] 主題切換 + 持久化 + 打勾
        [RelayCommand]
        private void SwitchTheme(string themeName)
        {
            ThemeService.Instance.SwitchTheme(themeName);
            AppSettings.Instance.UpdateTheme(themeName);
            IsThemeDefault = themeName == "Default";
            IsThemeIndustrial = themeName == "Industrial";
            IsThemeCyber = themeName == "Cyber";
            AlarmService.Instance.AddLog("INFO", $"Theme switched to {themeName}");
        }

        // [2026-03-12] 更新加工進度預估（由 StatusTimer_Tick 每次輪詢後呼叫）
        private void UpdateMachiningProgress()
        {
            int current = Status.CurrentLine;
            int total = Status.ProgramTotalLines;

            if (total > 0 && current > 0)
            {
                MachiningProgressPercent = Math.Min((double)current / total * 100, 100);

                if (Status.InterpState == "RUNNING" && _cycleTimer.IsEnabled)
                {
                    var elapsed = DateTime.Now - _cycleStartTime;
                    if (current > 1 && elapsed.TotalSeconds > 2)
                    {
                        double remaining = elapsed.TotalSeconds * (total - current) / current;
                        var ts = TimeSpan.FromSeconds(remaining);
                        EstimatedRemainingDisplay = ts.TotalHours >= 1
                            ? $"{(int)ts.TotalHours}h {ts.Minutes:D2}m"
                            : $"{ts.Minutes}m {ts.Seconds:D2}s";
                    }
                }
            }
            else
            {
                MachiningProgressPercent = 0;
                EstimatedRemainingDisplay = "--";
            }
        }

        // [2026-03-12] 定期從後端拉取加工統計（每 10 秒）
        private int _statsCounter;
        private async Task PollMachiningStats()
        {
            _statsCounter++;
            if (_statsCounter % 20 != 0) return;

            try
            {
                var stats = await MachineControlService.Instance.GetMachiningStatsAsync();
                if (stats != null)
                {
                    TotalMachiningTimeDisplay = stats.TotalTimeDisplay;
                    TotalCycleCount = stats.CycleCount;
                }
            }
            catch { /* 非關鍵，靜默 */ }
        }

        // [2026-03-12] 開機檢查斷電續切狀態
        private async Task CheckResumeState()
        {
            try
            {
                var state = await MachineControlService.Instance.GetResumeStateAsync();
                if (state != null && !string.IsNullOrEmpty(state.File) && state.Line > 0)
                {
                    var fileName = System.IO.Path.GetFileName(state.File);
                    var msg = $"偵測到中斷的加工作業：\n\n" +
                              $"檔案：{fileName}\n" +
                              $"中斷行號：{state.Line}\n" +
                              $"刀具：T{state.Tool}\n" +
                              $"時間：{state.TimestampDisplay}\n\n" +
                              $"是否從行號 {state.Line} 繼續加工？";

                    var result = System.Windows.MessageBox.Show(msg, "斷電續切",
                        System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);

                    if (result == System.Windows.MessageBoxResult.Yes)
                    {
                        AlarmService.Instance.AddLog("INFO", $"斷電續切：從 {fileName} 第 {state.Line} 行恢復");
                        await MachineControlService.Instance.RunFromLineAsync(state.File, state.Line);
                    }
                    else
                    {
                        await MachineControlService.Instance.ClearResumeStateAsync();
                        AlarmService.Instance.AddLog("INFO", "使用者取消斷電續切");
                    }
                }
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("DEBUG", $"Resume check: {ex.Message}");
            }
        }
    }
}
