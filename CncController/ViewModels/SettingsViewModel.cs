using System.Collections.Generic;
using System.Threading.Tasks;
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

        // ★★★ 權限控制 ★★★
        [ObservableProperty]
        private bool _canEditHardware;

        public SettingsViewModel()
        {
            // 監聽硬體掃描結果，連動更新 Mapping 的下拉選單
            HardwareVM.Slaves.CollectionChanged += (s, e) =>
            {
                MappingVM.UpdateSlaves(HardwareVM.Slaves);
            };

            // 監聽使用者變更
            AuthService.Instance.CurrentUserChanged += OnUserChanged;
            OnUserChanged(AuthService.Instance.CurrentUser);
        }

        private void OnUserChanged(User user)
        {
            if (user != null)
            {
                // 只有 Admin 或 Developer 可以編輯
                CanEditHardware = (user.Role == UserRole.Admin || user.Role == UserRole.Developer);
            }
        }

        // ★★★ 初始化：由 MainViewModel 呼叫 ★★★
        public void Initialize(MachineConfig config, List<DiscoveredSlave> slaves)
        {
            // 1. 顯示掃描結果
            HardwareVM.Slaves.Clear();
            foreach (var s in slaves) HardwareVM.Slaves.Add(s);

            // 2. 還原軸參數 (若設定檔是空的，則產生預設值)
            AxisVM.Axes.Clear();
            if (config.Axes != null && config.Axes.Count > 0)
            {
                foreach (var axis in config.Axes) AxisVM.Axes.Add(axis);
            }
            else
            {
                // ★★★ 防止列表空白：預設產生 X, Y, Z ★★★
                AxisVM.Axes.Add(new AxisSetting { AxisID = "X", Name = "X Axis" });
                AxisVM.Axes.Add(new AxisSetting { AxisID = "Y", Name = "Y Axis" });
                AxisVM.Axes.Add(new AxisSetting { AxisID = "Z", Name = "Z Axis" });
            }

            // 3. 還原對應表
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
            DeployStatus = "Generating Config...";

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

            await ConfigurationService.Instance.SaveConfigAsync(config);

            DeployStatus = "Config Saved & Restarting...";
            await Task.Delay(1000);
            DeployStatus = "Ready.";
        }
    }
}