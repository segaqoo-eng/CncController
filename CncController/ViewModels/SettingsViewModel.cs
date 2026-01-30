using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CncController.Services;
using CncController.Models;
using System.Linq;
// [新增] 引用 Brush 資源
using System.Windows.Media;

namespace CncController.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        public HardwareDiscoveryViewModel HardwareVM { get; } = new();
        public AxisParameterViewModel AxisVM { get; } = new();
        public MachineConfigViewModel MachineConfigVM { get; } = new();
        public AxisMappingViewModel MappingVM { get; } = new();

        [ObservableProperty]
        private string _deployStatus = "Ready";

        [ObservableProperty]
        private bool _canEditHardware;

        // 用於顯示錯誤日誌
        [ObservableProperty]
        private string _lastErrorLog;

        // [新增] 掃描驗證狀態
        [ObservableProperty]
        private string _scanResultText = "Not Verified";

        [ObservableProperty]
        private Brush _scanResultColor = Brushes.Gray;

        public SettingsViewModel()
        {
            // 1. 內部連動：當 HardwareVM 的 Slaves 變動時，通知 MappingVM 更新選項
            HardwareVM.Slaves.CollectionChanged += (s, e) =>
            {
                MappingVM.UpdateSlaves(HardwareVM.Slaves);
            };

            // 2. 權限管理
            AuthService.Instance.CurrentUserChanged += OnUserChanged;
            OnUserChanged(AuthService.Instance.CurrentUser);

            // 3. 與 MainViewModel 連動
            try
            {
                var app = System.Windows.Application.Current;
                if (app?.MainWindow?.DataContext is MainViewModel mainVM)
                {
                    // [A] 訂閱事件 (處理未來的掃描)
                    mainVM.HardwareValidationCompleted += OnHardwareValidationCompleted;

                    // [B] ★★★ 讀取現有的掃描結果 ★★★
                    // 如果 MainViewModel 已經有上次掃描的緩存，直接呼叫您的處理函式
                    if (mainVM.LastValidatedSlaves != null && mainVM.LastValidatedSlaves.Count > 0)
                    {
                        // 直接重用您寫好的方法！
                        // 注意：需要傳入 Slaves 和 Config，這兩個 MainVM 都有存
                        OnHardwareValidationCompleted(mainVM.LastValidatedSlaves, mainVM.LastValidatedConfig);
                    }
                }
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("ERR", $"Failed to subscribe hardware validation event: {ex.Message}");
            }
        }

        private void OnUserChanged(User user)
        {
            if (user != null)
            {
                CanEditHardware = (user.Role == UserRole.Admin || user.Role == UserRole.Developer);
            }
        }

        // [新增] 硬體驗證完成事件處理方法
        // 當 MainViewModel 掃描並驗證完成後，此方法會被呼叫
        // 用途：自動填充 HARDW
        // ARE SCAN 表格與 AXIS MAPPING 清單
        // [SettingsViewModel.cs]
        private void OnHardwareValidationCompleted(List<DiscoveredSlave> slaves, MachineConfig config)
        {
            try
            {
                // 1. 確保 UI 執行緒 (如果是從非 UI 執行緒呼叫)
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    // Step A: 填充 HARDWARE SCAN 表格 (顯示抓到的 Slave)
                    HardwareVM.Slaves.Clear();
                    foreach (var slave in slaves)
                    {
                        HardwareVM.Slaves.Add(slave);
                    }

                    // Step B: 載入 AXIS MAPPING (載入軟體設定)
                    // 這邊會把 Config 裡的設定填入 MappingVM
                    MappingVM.LoadMapping(config, slaves);

                    // Step C: 初始化軸參數頁面
                    AxisVM.Axes.Clear();
                    if (config.Axes != null)
                    {
                        foreach (var axis in config.Axes) AxisVM.Axes.Add(axis);
                    }

                    // ★★★ [關鍵修改] 立即執行一次比對，更新 Settings 頁面的狀態文字 ★★★
                    // 這樣管理者一進來，就會看到紅字顯示具體哪裡錯了
                    VerifyHardware(config, slaves);
                });
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("ERR", $"Error updating settings from validation: {ex.Message}");
            }
        }
        // [新增] 事件處理方法
        /*private void OnHardwareValidationCompleted(List<DiscoveredSlave> slaves, MachineConfig config)
        {
            // 確保在 UI 執行緒更新
            Application.Current.Dispatcher.Invoke(() =>
            {
                HardwareVM.Slaves.Clear();
                foreach (var slave in slaves)
                {
                    HardwareVM.Slaves.Add(slave);
                }
                HardwareVM.ScanStatus = "Scan Completed.";
            });
        }
        */
        public void Initialize(MachineConfig config, List<DiscoveredSlave> slaves)
        {
            HardwareVM.Slaves.Clear();
            foreach (var s in slaves) HardwareVM.Slaves.Add(s);

            AxisVM.Axes.Clear();
            if (config.Axes != null && config.Axes.Count > 0)
            {
                for (int i = 0; i < config.Axes.Count; i++)
                {
                    config.Axes[i].Index = i;
                    AxisVM.Axes.Add(config.Axes[i]);
                }
            }
            else
            {
                AxisVM.Axes.Add(new AxisSetting { Index = 0, AxisID = "X", Name = "X Axis" });
                AxisVM.Axes.Add(new AxisSetting { Index = 1, AxisID = "Y", Name = "Y Axis" });
                AxisVM.Axes.Add(new AxisSetting { Index = 2, AxisID = "Z", Name = "Z Axis" });
            }

            MappingVM.LoadMapping(config, slaves);

            // [新增] 初始化時自動執行一次驗證 (如果已經有 Config)
            if (config.Mappings.Count > 0)
            {
                VerifyHardware(config, slaves);
            }
        }

       

        // [新增] 驗證邏輯封裝
        private void VerifyHardware(MachineConfig config, List<DiscoveredSlave> slaves)
        {
            var result = HardwareScanService.Instance.ValidateTopology(slaves, config);

            ScanResultText = result.Message;
            if (result.IsValid)
            {
                ScanResultColor = Brushes.LimeGreen;
                // AlarmService.Instance.AddLog("SYS", "Hardware Verified OK");
            }
            else
            {
                ScanResultColor = Brushes.Red;
                AlarmService.Instance.AddLog("WARN", result.Message);
            }
        }

        [RelayCommand]
        private void ApplyMachineConfig()
        {
            MappingVM.UpdateSlaves(HardwareVM.Slaves);
            MappingVM.GenerateAxisTable(MachineConfigVM);
        }

        [RelayCommand]
        private async Task GenerateAndDeploy()
        {
            try
            {
                DeployStatus = "Saving Config...";

                var config = new MachineConfig();
                config.Axes.AddRange(AxisVM.Axes);

                // [修正] 清單重建：先清除再新增（避免重複）
                config.Mappings.Clear();
                foreach (var mapItem in MappingVM.AxisMaps)
                {
                    if (mapItem.SelectedSlave != null)
                    {
                        config.Mappings.Add(new HardwareMapping
                        {
                            LogicalName = mapItem.AxisName,
                            PhysicalAddress = mapItem.SelectedSlave.Name,
                            PhysicalIndex = mapItem.SelectedSlave.Index,
                            // [關鍵] 儲存時，將目前的 VID/PID 寫入 Config，作為未來的驗證標準
                            ExpectedVendorId = mapItem.SelectedSlave.VendorId,
                            ExpectedProductCode = mapItem.SelectedSlave.ProductCode
                        });
                    }
                }

                // 1. 存檔並觸發重啟
                await ConfigurationService.Instance.SaveConfigAsync(config);

                DeployStatus = "Restarting LinuxCNC...";

                // 2. 開始輪詢確認啟動狀態
                bool isStarted = await WaitForLinuxCNC(20);

                if (isStarted)
                {
                    DeployStatus = "Online (Ready)";
                    // [新增] 部署成功後，重新驗證一次狀態
                    VerifyHardware(config, HardwareVM.Slaves.ToList());
                }
                else
                {
                    DeployStatus = "Startup FAILED";
                    string log = await MachineControlService.Instance.GetStartupLogAsync();
                    LastErrorLog = log;
                    Console.WriteLine("STARTUP ERROR LOG:\n" + log);
                }
            }
            catch (Exception ex)
            {
                DeployStatus = $"Error: {ex.Message}";
            }
        }

        private async Task<bool> WaitForLinuxCNC(int timeoutSeconds)
        {
            for (int i = 0; i < timeoutSeconds; i++)
            {
                await Task.Delay(1000);
                DeployStatus = $"Starting... ({i}/{timeoutSeconds}s)";

                bool connected = await MachineControlService.Instance.CheckConnectionAsync();
                if (connected)
                {
                    return true;
                }
            }
            return false;
        }
    }
}