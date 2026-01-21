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

        // ★★★ 修正：確認使用正確的類別名稱 AxisParameterViewModel ★★★
        public AxisParameterViewModel AxisVM { get; } = new();

        public MachineConfigViewModel MachineConfigVM { get; } = new();
        public AxisMappingViewModel MappingVM { get; } = new();

        [ObservableProperty]
        private string _deployStatus = "Ready";

        public SettingsViewModel()
        {
            HardwareVM.Slaves.CollectionChanged += (s, e) =>
            {
                MappingVM.UpdateSlaves(HardwareVM.Slaves);
            };
        }

        [RelayCommand]
        private void ApplyMachineConfig()
        {
            MappingVM.UpdateSlaves(HardwareVM.Slaves);
            // 請確認 MappingVM 裡面的方法名稱是 GenerateAxisTable 還是 UpdateAxisRows
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

                        // ★★★ 修正：補上嚴格比對所需的欄位 ★★★
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