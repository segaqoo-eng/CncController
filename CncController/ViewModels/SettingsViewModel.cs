using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows; // 如果是用 WPF，用於 MessageBox
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CncController.Services;
using CncController.Models;
using System.Linq;

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

        // 用於顯示錯誤日誌 (若需要彈窗顯示)
        [ObservableProperty]
        private string _lastErrorLog;

        public SettingsViewModel()
        {
            HardwareVM.Slaves.CollectionChanged += (s, e) =>
            {
                MappingVM.UpdateSlaves(HardwareVM.Slaves);
            };

            AuthService.Instance.CurrentUserChanged += OnUserChanged;
            OnUserChanged(AuthService.Instance.CurrentUser);
        }

        private void OnUserChanged(User user)
        {
            if (user != null)
            {
                CanEditHardware = (user.Role == UserRole.Admin || user.Role == UserRole.Developer);
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

                foreach (var mapItem in MappingVM.AxisMaps)
                {
                    if (mapItem.SelectedSlave != null)
                    {
                        config.Mappings.Add(new HardwareMapping
                        {
                            LogicalName = mapItem.AxisName,
                            PhysicalAddress = mapItem.SelectedSlave.Name,
                            PhysicalIndex = mapItem.SelectedSlave.Index,
                            ExpectedVendorId = mapItem.SelectedSlave.VendorId,
                            ExpectedProductCode = mapItem.SelectedSlave.ProductCode
                        });
                    }
                }

                // 1. 存檔並觸發重啟
                await ConfigurationService.Instance.SaveConfigAsync(config);

                DeployStatus = "Restarting LinuxCNC...";

                // 2. 開始輪詢確認啟動狀態
                bool isStarted = await WaitForLinuxCNC(20); // 等待 20 秒

                if (isStarted)
                {
                    DeployStatus = "Online (Ready)";
                    // 可選：成功後自動跳轉或彈出通知
                }
                else
                {
                    DeployStatus = "Startup FAILED";
                    // 3. 失敗時抓取 Log
                    string log = await MachineControlService.Instance.GetStartupLogAsync();
                    LastErrorLog = log;

                    // 這裡可以用 MessageBox 或 Dialog 顯示 log
                    // MessageBox.Show($"LinuxCNC Failed to Start:\n\n{log}", "Error");
                    Console.WriteLine("STARTUP ERROR LOG:\n" + log);
                }
            }
            catch (Exception ex)
            {
                DeployStatus = $"Error: {ex.Message}";
            }
        }

        // ★★★ 核心邏輯：輪詢等待 LinuxCNC 啟動 ★★★
        private async Task<bool> WaitForLinuxCNC(int timeoutSeconds)
        {
            for (int i = 0; i < timeoutSeconds; i++)
            {
                // 每秒檢查一次
                await Task.Delay(1000);

                // 更新 UI 倒數 (可選)
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