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
            HardwareVM.Slaves.CollectionChanged += (s, e) =>
            {
                MappingVM.UpdateSlaves(HardwareVM.Slaves);
            };

            AuthService.Instance.CurrentUserChanged += OnUserChanged;
            OnUserChanged(AuthService.Instance.CurrentUser);

            // [新增] 訂閱 MainViewModel 的硬體驗證完成事件
            // 當硬體驗證成功完成時，會自動填充 HARDWARE SCAN 與 AXIS MAPPING
            try
            {
                var app = System.Windows.Application.Current;
                if (app?.MainWindow?.DataContext is MainViewModel mainVM)
                {
                    mainVM.HardwareValidationCompleted += OnHardwareValidationCompleted;
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
        // 用途：自動填充 HARDWARE SCAN 表格與 AXIS MAPPING 清單
        private void OnHardwareValidationCompleted(List<DiscoveredSlave> slaves, MachineConfig config)
        {
            try
            {
                // Step 1: 填充 HARDWARE SCAN 表格（Slaves 清單）
                HardwareVM.Slaves.Clear();
                foreach (var slave in slaves)
                {
                    HardwareVM.Slaves.Add(slave);
                }

                // Step 2: 載入 AXIS MAPPING（根據已保存的設定顯示軸與硬體的對應）
                MappingVM.LoadMapping(config, slaves);

                // Step 3: 根據軸參數初始化軸清單
                AxisVM.Axes.Clear();
                if (config.Axes != null && config.Axes.Count > 0)
                {
                    for (int i = 0; i < config.Axes.Count; i++)
                    {
                        config.Axes[i].Index = i;
                        AxisVM.Axes.Add(config.Axes[i]);
                    }
                }

                // Step 4: 記錄日誌
                AlarmService.Instance.AddLog("SYS", "Hardware configuration loaded from file. HARDWARE SCAN and AXIS MAPPING updated.");
                ScanResultText = "Configuration loaded successfully";
                ScanResultColor = Brushes.LimeGreen;
            }
            catch (Exception ex)
            {
                AlarmService.Instance.AddLog("ERR", $"Error loading hardware validation data: {ex.Message}");
                ScanResultText = "Error loading configuration";
                ScanResultColor = Brushes.Red;
            }
        }

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