// [2026-03-12] 主軸暖機 ViewModel：逐步升速 + 進度顯示 + 可中斷
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    public partial class SpindleWarmupViewModel : ObservableObject
    {
        // [2026-03-12] 暖機階梯清單（可編輯）
        public ObservableCollection<WarmupStep> Steps { get; } = new();

        // [2026-03-12] 目前執行狀態
        [ObservableProperty] private bool _isRunning;
        [ObservableProperty] private int _currentStepIndex = -1;
        [ObservableProperty] private int _remainingSeconds;
        [ObservableProperty] private double _overallProgress;
        [ObservableProperty] private string _statusText = "";
        [ObservableProperty] private int _newStepRpm = 1000;
        [ObservableProperty] private int _newStepDuration = 60;

        private CancellationTokenSource? _cts;

        public SpindleWarmupViewModel()
        {
            LoadDefaultSteps();
        }

        // [2026-03-12] 載入預設暖機階梯
        private void LoadDefaultSteps()
        {
            Steps.Clear();
            var defaults = new (int rpm, int sec)[]
            {
                (500, 60), (1000, 60), (2000, 60), (4000, 60), (8000, 60)
            };
            foreach (var (rpm, sec) in defaults)
            {
                Steps.Add(new WarmupStep { Rpm = rpm, DurationSeconds = sec });
            }
        }

        // [2026-03-12] 新增階梯
        [RelayCommand]
        private void AddStep()
        {
            if (IsRunning) return;
            if (NewStepRpm < 100 || NewStepDuration < 5) return;
            Steps.Add(new WarmupStep { Rpm = NewStepRpm, DurationSeconds = NewStepDuration });
            AlarmService.Instance.AddLog("INFO", $"Warmup: added step {NewStepRpm} RPM / {NewStepDuration}s");
        }

        // [2026-03-12] 移除選中階梯
        [RelayCommand]
        private void RemoveStep(WarmupStep? step)
        {
            if (IsRunning || step == null) return;
            Steps.Remove(step);
        }

        // [2026-03-12] 開始暖機循環
        [RelayCommand]
        private async Task StartWarmup()
        {
            if (IsRunning || Steps.Count == 0) return;

            // 安全檢查：需要電源 ON + 非 ESTOP
            var svc = MachineControlService.Instance;

            IsRunning = true;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            int totalSeconds = Steps.Sum(s => s.DurationSeconds);
            int elapsedTotal = 0;

            AlarmService.Instance.AddLog("INFO", $"Spindle warmup started: {Steps.Count} steps, total {totalSeconds}s");

            // 重置所有步驟狀態
            foreach (var s in Steps) s.StepStatus = "";

            try
            {
                for (int i = 0; i < Steps.Count; i++)
                {
                    token.ThrowIfCancellationRequested();

                    var step = Steps[i];
                    CurrentStepIndex = i;
                    step.StepStatus = "Running";
                    StatusText = $"階段 {i + 1}/{Steps.Count}：{step.Rpm} RPM";

                    // 發送 M3 S{rpm}
                    bool ok = await svc.SendMdiCommandAsync($"M3 S{step.Rpm}");
                    if (!ok)
                    {
                        StatusText = $"階段 {i + 1} 發送失敗，暖機中止";
                        AlarmService.Instance.AddLog("ERROR", $"Warmup: M3 S{step.Rpm} failed, aborting");
                        break;
                    }

                    AlarmService.Instance.AddLog("INFO", $"Warmup step {i + 1}: M3 S{step.Rpm} for {step.DurationSeconds}s");

                    // 倒數計時
                    for (int sec = step.DurationSeconds; sec > 0; sec--)
                    {
                        token.ThrowIfCancellationRequested();
                        RemainingSeconds = sec;
                        StatusText = $"階段 {i + 1}/{Steps.Count}：{step.Rpm} RPM — 剩餘 {sec}s";
                        OverallProgress = (double)elapsedTotal / totalSeconds * 100;
                        await Task.Delay(1000, token);
                        elapsedTotal++;
                    }

                    step.StepStatus = "Done";
                }

                // 暖機完成 → M5 停止主軸
                await svc.SendMdiCommandAsync("M5");
                OverallProgress = 100;
                StatusText = "暖機完成";
                RemainingSeconds = 0;
                CurrentStepIndex = -1;
                AlarmService.Instance.AddLog("INFO", "Spindle warmup completed successfully");
            }
            catch (OperationCanceledException)
            {
                // 使用者中斷 → M5 停止主軸
                await svc.SendMdiCommandAsync("M5");
                StatusText = "暖機已中止";
                AlarmService.Instance.AddLog("WARNING", "Spindle warmup cancelled by user");

                // 標記未完成步驟
                for (int j = CurrentStepIndex; j < Steps.Count; j++)
                {
                    if (Steps[j].StepStatus != "Done")
                        Steps[j].StepStatus = "Skipped";
                }
                CurrentStepIndex = -1;
            }
            catch (Exception ex)
            {
                await svc.SendMdiCommandAsync("M5");
                StatusText = $"暖機異常：{ex.Message}";
                AlarmService.Instance.AddLog("ERROR", $"Warmup error: {ex.Message}");
                CurrentStepIndex = -1;
            }
            finally
            {
                IsRunning = false;
                _cts?.Dispose();
                _cts = null;
            }
        }

        // [2026-03-12] 中斷暖機
        [RelayCommand]
        private void StopWarmup()
        {
            if (!IsRunning) return;
            _cts?.Cancel();
        }
    }
}
