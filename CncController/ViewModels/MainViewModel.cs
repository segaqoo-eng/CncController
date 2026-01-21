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
}