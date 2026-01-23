using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel; // 必須引用
using CommunityToolkit.Mvvm.Input;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    // ★★★ 關鍵：必須是 partial class ★★★
    public partial class MainViewModel : ObservableObject
    {
        // === 子 ViewModels ===
        public MonitorViewModel MonitorVM { get; } = new MonitorViewModel();
        public SettingsViewModel SettingsVM { get; } = new SettingsViewModel();

        // === 導航屬性 ===
        [ObservableProperty]
        private object _currentViewModel;

        // ★★★ 關鍵：定義這個欄位，系統會自動產生 SystemStatus 屬性 ★★★
        [ObservableProperty]
        private string _systemStatus = "Initializing...";

        [ObservableProperty]
        private bool _isSystemReady = false;

        [ObservableProperty]
        private User _currentUser;

        // === Dashboard 狀態 ===
        [ObservableProperty] private bool _isConnected;
        [ObservableProperty] private bool _isEstop;
        [ObservableProperty] private bool _isPower;

        // === Cycle Control 顯示時間 ===
        [ObservableProperty] private string _cycleTimeDisplay = "00:00:00";

        private readonly DispatcherTimer _statusTimer;

        public MainViewModel()
        {
            // 導航預設
            CurrentViewModel = MonitorVM;

            // 使用者
            AuthService.Instance.CurrentUserChanged += user => CurrentUser = user;
            CurrentUser = AuthService.Instance.CurrentUser;

            // 系統初始化
            InitializeSystemAsync();

            // 啟動輪詢 (0.5秒)
            _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _statusTimer.Tick += async (s, e) => await PollMachineStatus();
            _statusTimer.Start();
        }

        private async void InitializeSystemAsync()
        {
            await Task.Delay(500);

            try
            {
                // 這裡存取 SystemStatus，如果報錯，請嘗試「重建方案」
                SystemStatus = $"Loading Config... (v{VersionConfig.CurrentVersion})";
                var config = await ConfigurationService.Instance.LoadConfigAsync();

                SystemStatus = "Scanning Bus...";
                var slaves = await HardwareScanService.Instance.ScanAsync();

                SystemStatus = "Validating...";
                bool isValid = ValidateHardware(config, slaves);

                SettingsVM.Initialize(config, slaves);

                if (isValid)
                {
                    SystemStatus = $"System Ready (v{VersionConfig.CurrentVersion})";
                    IsSystemReady = true;
                    AlarmService.Instance.AddLog("INFO", "System startup passed.");
                }
                else
                {
                    SystemStatus = "Hardware Mismatch";
                    IsSystemReady = false;
                }
            }
            catch (Exception ex)
            {
                SystemStatus = $"Error: {ex.Message}";
                IsSystemReady = false;
            }
        }

        private bool ValidateHardware(MachineConfig config, List<DiscoveredSlave> slaves)
        {
            if (config.Mappings == null || config.Mappings.Count == 0) return false;
            return true;
        }

        private async Task PollMachineStatus()
        {
            try
            {
                bool connected = await MachineControlService.Instance.CheckConnectionAsync();
                IsConnected = connected;

                if (IsConnected)
                {
                    if (SystemStatus.StartsWith("Offline"))
                        SystemStatus = $"System Ready (v{VersionConfig.CurrentVersion})";
                }
                else
                {
                    if (IsSystemReady) SystemStatus = "Offline (Reconnecting...)";
                    IsPower = false;
                }
            }
            catch
            {
                IsConnected = false;
            }
        }

        // === 按鈕命令區 ===

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
        private async Task CycleStart()
        {
            if (!IsEstop && IsPower)
                await MachineControlService.Instance.CycleStartAsync();
        }

        [RelayCommand]
        private async Task Stop() => await MachineControlService.Instance.StopAsync();

        [RelayCommand]
        private async Task Reload() => await MachineControlService.Instance.ReloadProgramAsync();

        [RelayCommand]
        private async Task FeedHold() => await MachineControlService.Instance.FeedHoldAsync(true);

        [RelayCommand]
        private async Task HomeAll() => await MachineControlService.Instance.HomeAxisAsync(-1);

        [RelayCommand]
        private void Navigate(string destination)
        {
            if (destination == "Settings") CurrentViewModel = SettingsVM;
            else CurrentViewModel = MonitorVM;
        }
    }
}


/*using System;
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

        // === 狀態與導航 ===
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

            // 啟動系統檢查 (您原本的邏輯)
            InitializeSystemAsync();

            // ★★★ [新增] 啟動狀態輪詢 (每 500ms 更新一次儀表板燈號) ★★★
            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _statusTimer.Tick += async (s, e) => await PollMachineStatus();
            _statusTimer.Start();
        }

        // === 1. 系統初始化 (保留您的邏輯) ===
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
                // 錯誤日誌已在 ValidateHardware 中寫入
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

        // === 2. 狀態輪詢 (新增：給 Dashboard 使用) ===
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

                    // TODO: 未來可在此呼叫 GetMachineStatusDetail() 來更新 IsEstop / IsPower 的真實狀態
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

        // === 3. 控制指令 (新增：給 DashboardPanel 按鈕綁定用) ===

        [RelayCommand]
        private async Task ToggleEstop()
        {
            IsEstop = !IsEstop; // 切換 UI 狀態
            await MachineControlService.Instance.SetEstopAsync(IsEstop);
        }

        [RelayCommand]
        private async Task TogglePower()
        {
            IsPower = !IsPower; // 切換 UI 狀態
            await MachineControlService.Instance.SetPowerAsync(IsPower);
        }

        [RelayCommand]
        private async Task HomeAll()
        {
            // -1 代表全部軸回原點
            await MachineControlService.Instance.HomeAxisAsync(-1);
        }

        // === 4. 導航 (保留您的邏輯) ===
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
*/