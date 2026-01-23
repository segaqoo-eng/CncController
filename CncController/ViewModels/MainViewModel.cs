using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Threading; // ★ 新增：用於計時器
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // === 子 ViewModels ===
        public MonitorViewModel MonitorVM { get; } = new MonitorViewModel();
        public SettingsViewModel SettingsVM { get; } = new SettingsViewModel();

        // === 導航與使用者 ===
        [ObservableProperty]
        private object _currentViewModel;

        [ObservableProperty]
        private string _systemStatus = "Initializing...";

        [ObservableProperty]
        private bool _isSystemReady = false;

        [ObservableProperty]
        private User _currentUser;

        // ★★★ [新增] Dashboard 儀表板需要的即時狀態 ★★★
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private bool _isEstop;  // true = 急停中
        [ObservableProperty] private bool _isPower;  // true = 電源開啟

        // ★★★ [新增] 狀態輪詢計時器 ★★★
        private readonly DispatcherTimer _statusTimer;

        public MainViewModel()
        {
            // 初始化導航到監控頁
            CurrentViewModel = MonitorVM;

            // 訂閱使用者變更
            AuthService.Instance.CurrentUserChanged += user => CurrentUser = user;
            CurrentUser = AuthService.Instance.CurrentUser;

            // 啟動系統檢查 (原本的邏輯)
            InitializeSystemAsync();

            // ★★★ [新增] 啟動狀態輪詢 (每 500ms 更新一次儀表板燈號) ★★★
            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _statusTimer.Tick += async (s, e) => await PollMachineStatus();
            _statusTimer.Start();
        }

        private async void InitializeSystemAsync()
        {
            await Task.Delay(500);

            // 顯示版本號以確認同步
            SystemStatus = $"Loading Config... (v{VersionConfig.CurrentVersion})";
            var config = await ConfigurationService.Instance.LoadConfigAsync();

            SystemStatus = "Scanning Bus...";
            var slaves = await HardwareScanService.Instance.ScanAsync();

            SystemStatus = "Validating...";
            bool isValid = ValidateHardware(config, slaves);

            // 把資料倒給 SettingsViewModel
            SettingsVM.Initialize(config, slaves);

            if (isValid)
            {
                SystemStatus = $"System Ready (v{VersionConfig.CurrentVersion})";
                IsSystemReady = true;
                AlarmService.Instance.AddLog("INFO", $"System startup passed. v{VersionConfig.CurrentVersion}");
            }
            else
            {
                SystemStatus = "Hardware Mismatch / Not Configured";
                IsSystemReady = false;
            }
        }

        private bool ValidateHardware(MachineConfig config, List<DiscoveredSlave> slaves)
        {
            if (config.Mappings == null || config.Mappings.Count == 0)
            {
                AlarmService.Instance.AddLog("WARN", "System not configured.", true);
                return false;
            }
            return true;
        }

        // ★★★ [新增] 輪詢機台狀態 (給 Dashboard 使用) ★★★
        private async Task PollMachineStatus()
        {
            try
            {
                // 呼叫 Service 檢查連線
                bool connected = await MachineControlService.Instance.CheckConnectionAsync();
                IsConnected = connected;

                if (IsConnected)
                {
                    // 若連線成功，且目前顯示為斷線，則恢復顯示 Ready
                    if (SystemStatus.StartsWith("Offline"))
                        SystemStatus = $"System Ready (v{VersionConfig.CurrentVersion})";
                }
                else
                {
                    // 若斷線，更新狀態
                    if (IsSystemReady) SystemStatus = "Offline (Reconnecting...)";
                    IsPower = false;
                }
            }
            catch
            {
                IsConnected = false;
            }
        }

        // ★★★ [新增] 控制指令 (給 DashboardPanel 按鈕綁定用) ★★★

        [RelayCommand]
        private async Task ToggleEstop()
        {
            IsEstop = !IsEstop;
            await MachineControlService.Instance.SetEstopAsync(IsEstop);
        }

        [RelayCommand]
        private async Task TogglePower()
        {
            IsPower = !IsPower;
            await MachineControlService.Instance.SetPowerAsync(IsPower);
        }

        [RelayCommand]
        private async Task HomeAll()
        {
            await MachineControlService.Instance.HomeAxisAsync(-1);
        }

        [RelayCommand]
        private void Navigate(string destination)
        {
            switch (destination)
            {
                case "Settings":
                    CurrentViewModel = SettingsVM;
                    break;
                case "Main":
                default:
                    CurrentViewModel = MonitorVM;
                    break;
            }
        }
    }
}
/*
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // === 子 ViewModels ===
        public MonitorViewModel MonitorVM { get; } = new MonitorViewModel();
        public SettingsViewModel SettingsVM { get; } = new SettingsViewModel();

        // === 狀態與導航 ===
        [ObservableProperty]
        private object _currentViewModel;

        [ObservableProperty]
        private string _systemStatus = "Initializing...";

        [ObservableProperty]
        private bool _isSystemReady = false;

        [ObservableProperty]
        private User _currentUser;

        public MainViewModel()
        {
            // 初始化導航到監控頁
            CurrentViewModel = MonitorVM;

            // 訂閱使用者變更
            AuthService.Instance.CurrentUserChanged += user => CurrentUser = user;
            CurrentUser = AuthService.Instance.CurrentUser;

            // 啟動系統檢查
            InitializeSystemAsync();
        }

        private async void InitializeSystemAsync()
        {
            await Task.Delay(500);

            SystemStatus = "Loading Config...";
            var config = await ConfigurationService.Instance.LoadConfigAsync();

            SystemStatus = "Scanning Bus...";
            var slaves = await HardwareScanService.Instance.ScanAsync();

            SystemStatus = "Validating...";
            bool isValid = ValidateHardware(config, slaves);

            // ★★★ [新增] 把資料倒給 SettingsViewModel，這樣切換過去時資料都在 ★★★
            SettingsVM.Initialize(config, slaves);

            if (isValid)
            {
                SystemStatus = "System Ready";
                IsSystemReady = true;
                AlarmService.Instance.AddLog("INFO", "System startup validation passed.");
            }
            else
            {
                SystemStatus = "Hardware Mismatch / Not Configured";
                IsSystemReady = false;
                // 具體錯誤已在 ValidateHardware 中寫入 AlarmService
            }
        }

        private bool ValidateHardware(MachineConfig config, List<DiscoveredSlave> slaves)
        {
            if (config.Mappings == null || config.Mappings.Count == 0)
            {
                AlarmService.Instance.AddLog("WARN", "System not configured.", true);
                return false;
            }
            // 這裡未來可以加入更詳細的比對邏輯
            return true;
        }

        [RelayCommand]
        private void Navigate(string destination)
        {
            switch (destination)
            {
                case "Settings":
                    CurrentViewModel = SettingsVM;
                    break;
                case "Main":
                default:
                    CurrentViewModel = MonitorVM;
                    break;
            }
        }
    }
}*/