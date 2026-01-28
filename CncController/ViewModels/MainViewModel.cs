using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using System.Windows.Threading;
using System.Windows.Media; // [重要] 必須引用，為了使用 Color 和 Brush
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

        // --- 系統狀態與燈號 ---

        [ObservableProperty] private string _systemStatus = "Initializing System...";
        [ObservableProperty] private bool _isSystemReady = false;

        // [修正] 重新命名為 SystemStatusColor 以符合 SystemStatus
        // 這裡存 Color 是為了給 DropShadowEffect 用
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SystemStatusBrush))] // 通知 Brush 更新
        private Color _systemStatusColor = Colors.Gray;

        // 這裡轉成 Brush 是為了給 Background 用
        public SolidColorBrush SystemStatusBrush => new SolidColorBrush(SystemStatusColor);

        // [新增] Server 版本顯示
        [ObservableProperty] private string _serverVersionDisplay = "---";

        [ObservableProperty]
        private User _currentUser = new User { Username = "Operator", Role = UserRole.Operator };

        // === JOG 設定 ===
        [ObservableProperty] private double _jogFeedrate = 1500.0;
        [ObservableProperty] private double _jogStepDistance = 0;

        private readonly DispatcherTimer _timer;

        // ==============================================================================
        // 2. 建構子
        // ==============================================================================

        public MainViewModel()
        {
            CurrentViewModel = new MonitorViewModel();

            // 系統啟動 Log
            AlarmService.Instance.AddLog("LOGIN", "System Started");

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += async (s, e) => await PollMachineStatus();
            _timer.Start();
        }

        // ==============================================================================
        // 3. 狀態輪詢 (邏輯修正版)
        // ==============================================================================

        private async Task PollMachineStatus()
        {
            // 呼叫 Service (確保 Service 層已修改為回傳 tuple)
            var (connectionState, data) = await MachineControlService.Instance.GetStatusAsync();

            // 更新版本顯示
            ServerVersionDisplay = MachineControlService.Instance.ServerVersion;

            switch (connectionState)
            {
                case MachineControlService.ConnectionState.Connected:
                    // === 1. 通訊正常 (HTTP 200) ===
                    IsConnected = true;

                    if (data != null)
                    {
                        // 更新狀態旗標
                        IsPower = (data.Task_State == "ON");
                        IsEstop = (data.Task_State == "ESTOP");
                        IsSystemReady = !IsEstop && IsPower;

                        // [關鍵邏輯] 決定燈號顏色
                        if (IsEstop)
                        {
                            SystemStatus = "EMERGENCY STOP ACTIVE";
                            SystemStatusColor = Colors.Orange; // 🟠 急停
                        }
                        else if (!IsPower)
                        {
                            SystemStatus = "Machine Power Off";
                            SystemStatusColor = Colors.Orange; // 🟠 斷電
                        }
                        else
                        {
                            SystemStatus = "System Ready";
                            SystemStatusColor = Colors.LimeGreen; // 🟢 就緒
                        }

                        // 更新座標與數據
                        UpdateMachineData(data);
                    }
                    else
                    {
                        // 異常：有連線但無資料
                        SystemStatus = "Data Error";
                        SystemStatusColor = Colors.Orange;
                    }
                    break;

                case MachineControlService.ConnectionState.ServerOnly:
                    // === 2. 僅後端連線 (HTTP 500 / LinuxCNC Offline) ===
                    IsConnected = false;
                    IsPower = false;
                    IsSystemReady = false;

                    SystemStatusColor = Colors.Orange; // 🟠 警告
                    SystemStatus = "Backend Connected (LinuxCNC Offline)";
                    break;

                case MachineControlService.ConnectionState.Disconnected:
                default:
                    // === 3. 完全斷線 ===
                    IsConnected = false;
                    IsPower = false;
                    IsSystemReady = false;

                    SystemStatusColor = Colors.Red; // 🔴 斷線
                    SystemStatus = "Disconnected from Server";
                    break;
            }
        }

        private void UpdateMachineData(MachineStatusData data)
        {
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

        [RelayCommand]
        private async Task JogStart(string args)
        {
            // [DEBUG] 加入這行，如果 Log 沒出現，表示按鈕綁定有問題
            // AlarmService.Instance.AddLog("DEBUG", $"Jog Trig: {args}"); 

            if (string.IsNullOrEmpty(args)) return;
            var parts = args.Split(',');

            if (parts.Length == 2 && int.TryParse(parts[0], out int axis) && double.TryParse(parts[1], out double dirSign))
            {
                double finalSpeed = Math.Abs(JogFeedrate) * (dirSign > 0 ? 1 : -1);
                double distance = JogStepDistance > 0 ? JogStepDistance : 0;

                // [DEBUG] 確認最終發送的數值
                AlarmService.Instance.AddLog("JOG", $"Axis:{axis} Spd:{finalSpeed} Dist:{distance}");

                await MachineControlService.Instance.JogAsync(axis, finalSpeed, distance);
            }
        }

        // [修改] JOG Stop 邏輯：只在連續模式下生效
        [RelayCommand]
        private async Task JogStop(string axisStr)
        {
            if (int.TryParse(axisStr, out int axis))
            {
                // [關鍵] 只有在連續模式 (JogStepDistance == 0) 才發送 Stop
                // 如果是單步模式，機器移動完固定距離會自動停，
                // 此時若滑鼠放開觸發 Stop，會導致單步移動未完成即停止 (截斷)。
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

        [RelayCommand] private async Task CycleStart() => await MachineControlService.Instance.CycleStartAsync();
        [RelayCommand] private async Task FeedHold() => await MachineControlService.Instance.FeedHoldAsync();
        [RelayCommand] private async Task Stop() => await MachineControlService.Instance.StopAsync();
    }
}