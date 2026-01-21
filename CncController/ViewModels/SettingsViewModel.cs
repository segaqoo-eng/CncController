using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CncController.Services;
using CncController.Models;
using System.Linq; // 用於資料處理

namespace CncController.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        // === 1. 子 ViewModels ===

        // 硬體掃描 (既有)
        public HardwareDiscoveryViewModel HardwareVM { get; } = new();

        // 參數設定 (既有)
        public AxisConfigViewModel AxisVM { get; } = new();

        // [新增] 機台架構設定 (勾選 X, Y, Z, A...)
        public MachineConfigViewModel MachineConfigVM { get; } = new();

        // [新增] 軸對應設定 (X軸 -> Slave 1)
        public AxisMappingViewModel MappingVM { get; } = new();


        // === 2. 狀態屬性 ===

        [ObservableProperty]
        private string _deployStatus = "";


        // === 3. 建構函式 (處理資料連動) ===
        public SettingsViewModel()
        {
            // [關鍵邏輯] 訂閱硬體掃描的變動
            // 當 HardwareVM 掃描到新裝置 (Slaves 變動) 時，自動更新 MappingVM 的下拉選單
            HardwareVM.Slaves.CollectionChanged += (s, e) =>
            {
                MappingVM.UpdateSlaves(HardwareVM.Slaves);
            };
        }


        // === 4. 命令 (Commands) ===

        /// <summary>
        /// [新增] 應用機台架構設定：當使用者勾選完軸數，按下「更新對應表」時執行
        /// </summary>
        [RelayCommand]
        private void ApplyMachineConfig()
        {
            // 1. 確保下拉選單有最新的硬體資料
            MappingVM.UpdateSlaves(HardwareVM.Slaves);

            // 2. 叫 MappingVM 根據 MachineConfigVM 的勾選狀態 (X, Y, Z...) 產生表格列
            MappingVM.GenerateAxisTable(MachineConfigVM);
        }

        /// <summary>
        /// 產生設定檔並部署 (既有邏輯擴充)
        /// </summary>
        [RelayCommand]
        private async Task GenerateAndDeploy()
        {
            DeployStatus = "Generating Config...";

            var config = new MachineConfig();

            // 1. 加入軸參數 (Pitch, Pulse...)
            // (注意: 這裡假設 AxisVM.Axes 已經配合軸數調整，或是全部寫入)
            config.Axes.AddRange(AxisVM.Axes);

            // 2. [新增] 加入硬體對應結果
            // 將 MappingVM 裡面的配對資料 (AxisMapItem) 轉存到 Config
            foreach (var mapItem in MappingVM.AxisMaps)
            {
                // 只儲存有選到硬體的軸
                if (mapItem.SelectedSlave != null)
                {
                    config.Mappings.Add(new HardwareMapping
                    {
                        LogicalName = mapItem.AxisName,           // 例如 "X Axis"
                        PhysicalAddress = mapItem.SelectedSlave.Name, // 或是用 mapItem.SelectedSlave.Index.ToString()
                        // 如果有其他 Slave 詳細資料也存進去
                    });
                }
            }

            // 3. 儲存並部署
            // (假設 ConfigurationService 已經支援儲存這些欄位)
            await ConfigurationService.Instance.SaveConfigAsync(config);

            DeployStatus = "Config Saved & Restarting...";

            // 模擬延遲讓使用者看到訊息
            await Task.Delay(1000);
            DeployStatus = "Ready.";
        }
    }
}