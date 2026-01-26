using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using CncController.Services;
using CncController.Models; // ★ 關鍵：引用 Models 命名空間

namespace CncController.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty] private object _currentViewModel;
        [ObservableProperty] private MachineStatus _status = new();
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private bool _isPower;
        [ObservableProperty] private bool _isEstop;
        [ObservableProperty] private string _systemStatus = "Ready";
        [ObservableProperty] private bool _isSystemReady = true;

        // 這裡使用的 User 現在明確指向 CncController.Models.User
        //[ObservableProperty] private User _currentUser = new User { Username = "Operator", Role = "Admin" };
        // ★★★ 修正處：使用 UserRole 枚舉，而非字串 ★★★
        [ObservableProperty]
        private User _currentUser = new User { Username = "Operator", Role = UserRole.Admin };

        private readonly DispatcherTimer _timer;

        public MainViewModel()
        {
            // 初始化先顯示 MonitorView
            CurrentViewModel = new MonitorViewModel();

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timer.Tick += async (s, e) => await PollMachineStatus();
            _timer.Start();

            //CurrentViewModel = new MonitorViewModel();
            CurrentUser = new User { Username = "Operator", Role = UserRole.Operator };

            // ★ 測試履歷：程式啟動時寫入一筆，驗證履歷介面是否正常
            AlarmService.Instance.AddLog("LOGIN", "System UI Started (Debug Mode)");
           

        }

        private async Task PollMachineStatus()
        {
            var data = await MachineControlService.Instance.GetStatusAsync();
            if (data != null)
            {
                IsConnected = true;
                IsPower = (data.Task_State == "ON");
                IsEstop = (data.Task_State == "ESTOP");
                IsSystemReady = !IsEstop;
                SystemStatus = IsEstop ? "EMERGENCY STOP" : "SYSTEM READY";

                if (data.Position != null)
                {
                    if (data.Position.ContainsKey("X")) Status.X = data.Position["X"];
                    if (data.Position.ContainsKey("Y")) Status.Y = data.Position["Y"];
                    if (data.Position.ContainsKey("Z")) Status.Z = data.Position["Z"];
                }
                Status.Feedrate = data.Feedrate;
                Status.SpindleSpeed = data.Spindle_Speed;
                Status.File = data.File;
            }
            else IsConnected = false;
        }

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
            if (string.IsNullOrEmpty(args)) return;
            var p = args.Split(',');
            if (p.Length == 2 && int.TryParse(p[0], out int axis) && double.TryParse(p[1], out double speed))
            {
                await MachineControlService.Instance.JogAsync(axis, speed);
            }
        }

        [RelayCommand]
        private async Task JogStop(string axis)
        {
            if (int.TryParse(axis, out int ax))
            {
                await MachineControlService.Instance.JogStopAsync(ax);
            }
        }

        [RelayCommand] private async Task CycleStart() => await MachineControlService.Instance.CycleStartAsync();
        [RelayCommand] private async Task FeedHold() => await MachineControlService.Instance.FeedHoldAsync();
        [RelayCommand] private async Task Stop() => await MachineControlService.Instance.StopAsync();
    }
}