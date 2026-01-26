using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using CncController.Services;
using CncController.Models;

namespace CncController.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // ==============================================================================
        // 1. 屬性定義
        // ==============================================================================

        [ObservableProperty] private object _currentViewModel;
        [ObservableProperty] private MachineStatus _status = new();

        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private bool _isPower;
        [ObservableProperty] private bool _isEstop;

        [ObservableProperty] private string _systemStatus = "Initializing System...";
        [ObservableProperty] private bool _isSystemReady = false;

        [ObservableProperty]
        private User _currentUser = new User { Username = "Operator", Role = UserRole.Operator };

        // === JOG 設定 ===
        // JOG 速度 (預設 1500)
        [ObservableProperty] private double _jogFeedrate = 1500.0;

        // JOG 距離 (0 = 連續, > 0 = 寸動距離)
        [ObservableProperty] private double _jogStepDistance = 0;

        private readonly DispatcherTimer _timer;

        // ==============================================================================
        // 2. 建構子
        // ==============================================================================

        public MainViewModel()
        {
            CurrentViewModel = new MonitorViewModel();

            // 測試履歷
            AlarmService.Instance.AddLog("LOGIN", "System Started");

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += async (s, e) => await PollMachineStatus();
            _timer.Start();
        }

        // ==============================================================================
        // 3. 狀態輪詢
        // ==============================================================================

        private async Task PollMachineStatus()
        {
            try
            {
                var data = await MachineControlService.Instance.GetStatusAsync();

                if (data != null)
                {
                    IsConnected = true;
                    IsPower = (data.Task_State == "ON");
                    IsEstop = (data.Task_State == "ESTOP");

                    IsSystemReady = !IsEstop;
                    SystemStatus = IsEstop ? "EMERGENCY STOP ACTIVE" : (IsPower ? "System Ready" : "Machine Power Off");

                    if (data.Position != null)
                    {
                        if (data.Position.TryGetValue("X", out double x)) Status.X = x;
                        if (data.Position.TryGetValue("Y", out double y)) Status.Y = y;
                        if (data.Position.TryGetValue("Z", out double z)) Status.Z = z;
                        if (data.Position.TryGetValue("A", out double a)) Status.A = a;
                        if (data.Position.TryGetValue("B", out double b)) Status.B = b;
                        if (data.Position.TryGetValue("C", out double c)) Status.C = c;
                    }

                    Status.Feedrate = data.Feedrate;
                    Status.SpindleSpeed = data.Spindle_Speed;
                    Status.File = string.IsNullOrEmpty(data.File) ? "No File Loaded" : data.File;
                }
                else
                {
                    IsConnected = false;
                    IsSystemReady = false;
                    SystemStatus = "Disconnected from Server";
                }
            }
            catch
            {
                IsConnected = false;
            }
        }

        // ==============================================================================
        // 4. 指令
        // ==============================================================================

        [RelayCommand]
        private void Navigate(string viewName)
        {
            switch (viewName)
            {
                case "Main": CurrentViewModel = new MonitorViewModel(); break;
                case "Settings": CurrentViewModel = new SettingsViewModel(); break;
                case "History": CurrentViewModel = new HistoryViewModel(); break;
            }
        }

        [RelayCommand] private async Task TogglePower() => await MachineControlService.Instance.ResetMachineAsync();
        [RelayCommand] private async Task ToggleEstop() => await MachineControlService.Instance.TriggerEstopAsync();

        // JOG 相關指令 (給 JogPanel.xaml.cs 呼叫用)
        [RelayCommand]
        private async Task JogStart(string args)
        {
            // args 格式: "axis,speed" (這裡的 speed 只決定方向正負)
            if (string.IsNullOrEmpty(args)) return;
            var parts = args.Split(',');

            if (parts.Length == 2 && int.TryParse(parts[0], out int axis) && double.TryParse(parts[1], out double dirSign))
            {
                // 1. 決定最終速度 (使用介面設定的 JogFeedrate，方向由按鈕決定)
                // 如果按鈕傳來的 speed 絕對值大於 1，我們就信賴按鈕 (Slider模式)
                // 如果是 1 或 -1，我們就用 JogFeedrate
                double finalSpeed;
                if (Math.Abs(dirSign) > 1.0)
                    finalSpeed = dirSign; // 使用 Slider 值
                else
                    finalSpeed = JogFeedrate * dirSign; // 使用 Config 值

                // 2. 決定模式 (連續 vs 寸動)
                if (JogStepDistance > 0)
                {
                    // 寸動: 傳入距離
                    await MachineControlService.Instance.JogAsync(axis, finalSpeed, JogStepDistance);
                }
                else
                {
                    // 連續: 距離為 0
                    await MachineControlService.Instance.JogAsync(axis, finalSpeed, 0);
                }
            }
        }

        [RelayCommand]
        private async Task JogStop(string axisStr)
        {
            if (int.TryParse(axisStr, out int axis))
            {
                // 只有在連續模式下才需要手動發送停止
                // 寸動模式會自己停，不過發送停止也無妨
                if (JogStepDistance == 0)
                {
                    await MachineControlService.Instance.JogStopAsync(axis);
                }
            }
        }

        // 設定 JOG 模式 (給 JogConfig.xaml 呼叫用)
        [RelayCommand]
        private void SetJogMode(string value)
        {
            if (double.TryParse(value, out double dist))
            {
                JogStepDistance = dist;
            }
        }

        [RelayCommand] private async Task CycleStart() => await MachineControlService.Instance.CycleStartAsync();
        [RelayCommand] private async Task FeedHold() => await MachineControlService.Instance.FeedHoldAsync();
        [RelayCommand] private async Task Stop() => await MachineControlService.Instance.StopAsync();
    }
}