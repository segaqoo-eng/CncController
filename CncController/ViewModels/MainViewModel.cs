using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CncController.Models;
using CncController.Services;

namespace CncController.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // === 1. 狀態屬性 ===

        [ObservableProperty]
        private string _systemStatus = "Initializing..."; // 狀態列文字

        [ObservableProperty]
        private bool _isSystemReady = false; // 是否允許操作 (綠燈/紅燈)

        [ObservableProperty]
        private User _currentUser; // 當前使用者 (給 UI 綁定用)

        // 給 UI 綁定的「即時警報清單」 (用來顯示閃爍橫幅)
        public ObservableCollection<AlarmItem> ActiveAlarms => AlarmService.Instance.ActiveAlarms;

        // === 2. 初始化 ===

        public MainViewModel()
        {
            // 訂閱使用者變更事件 (當 AuthService 登入/登出時，這裡會收到通知)
            AuthService.Instance.CurrentUserChanged += user => CurrentUser = user;
            CurrentUser = AuthService.Instance.CurrentUser; // 初始狀態

            // 啟動系統初始化流程
            InitializeSystemAsync();
        }

        private async void InitializeSystemAsync()
        {
            await Task.Delay(500); // 稍微緩衝，讓 UI 顯示出來

            // 步驟 A: 讀取設定檔
            SystemStatus = "Loading Configuration...";
            var config = await ConfigurationService.Instance.LoadConfigAsync();

            // 步驟 B: 掃描硬體
            SystemStatus = "Scanning Hardware Bus...";
            var slaves = await HardwareScanService.Instance.ScanAsync(); // 假設 Service 有回傳 List<DiscoveredSlave>

            // 步驟 C: 驗證匹配
            SystemStatus = "Validating System...";
            bool isValid = ValidateHardware(config, slaves);

            if (isValid)
            {
                SystemStatus = "System Ready";
                IsSystemReady = true;
                AlarmService.Instance.AddLog("INFO", "System startup validation passed.");
            }
            else
            {
                SystemStatus = "Hardware Mismatch / Config Missing";
                IsSystemReady = false;
                // 注意：具體的錯誤訊息已經在 ValidateHardware 裡寫入 AlarmService 了
            }
        }

        /// <summary>
        /// 核心邏輯：比對設定檔與實際硬體 (嚴格模式)
        /// </summary>
        private bool ValidateHardware(MachineConfig config, List<DiscoveredSlave> slaves)
        {
            // 狀況 1: 完全沒設定檔
            if (config.Mappings == null || config.Mappings.Count == 0)
            {
                AlarmService.Instance.AddLog("WARN", "System not configured. Please login as Admin to setup.", true);
                return false;
            }

            bool allMatch = true;

            foreach (var map in config.Mappings)
            {
                // 1. 找站號 (Index)
                var slave = slaves.FirstOrDefault(s => s.Index == map.PhysicalIndex);

                if (slave == null)
                {
                    AlarmService.Instance.AddLog("ALARM", $"Missing Device: {map.LogicalName} (Station #{map.PhysicalIndex}) not found!", true);
                    allMatch = false;
                    continue;
                }

                // 2. 比對廠商 ID (嚴格檢查)
                // 注意：如果設定檔裡的 ExpectedVendorId 是空的 (舊檔案)，則跳過此檢查
                if (!string.IsNullOrEmpty(map.ExpectedVendorId) && slave.VendorId != map.ExpectedVendorId)
                {
                    AlarmService.Instance.AddLog("ALARM", $"Type Mismatch: {map.LogicalName} expected VendorID '{map.ExpectedVendorId}', but found '{slave.VendorId}'.", true);
                    allMatch = false;
                }
            }

            return allMatch;
        }

        // === 3. 命令 (Commands) ===

        [RelayCommand]
        private void Login(string password)
        {
            // 嘗試登入 (成功與否由 Service 決定)
            bool success = AuthService.Instance.Login(password);
            if (!success)
            {
                // 可以選擇在這裡跳出小提示，或者單純不動作
            }
        }

        [RelayCommand]
        private void Logout()
        {
            AuthService.Instance.Logout();
        }

        [RelayCommand]
        private void AcknowledgeAlarms()
        {
            // 解除所有警報 (按下 ESC 時觸發)
            AlarmService.Instance.AcknowledgeAll();

            // 如果解除後想重試驗證，可以再呼叫一次 InitializeSystemAsync()
            // 這裡我們先保持簡單，只清除訊息
        }
    }
}